using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Pragma.CommandExecutor.Tests
{
    /// <summary>
    /// Hot path timings (see <i>Window → Analysis → Performance Test Report</i>) and zero allocation checks.
    /// Editor timings depend on the machine and the Editor code optimization mode: compare a change against a run
    /// of the previous code on the same machine, never against absolute numbers.
    /// </summary>
    [Category("Performance")]
    public class CommandExecutorPerformanceTests
    {
        // Exactly representable in binary, so accumulated delays never drift.
        private const float FRAME = 1f / 64f;
        private const int TREE_COUNT = 1000;
        private const int INSTANT_TREE_COUNT = 10000;
        private const int WARMUP_FRAMES = 300;
        private const int MEASURED_FRAMES = 100;
        private const int MAX_FRAMES_TO_COMPLETE = 1000;

        private static readonly Action Noop = () => { };

        private CommandExecutor _executor;
        private ICommand _prebuiltTree;
        private Action _done;
        private int _completed;

        [SetUp]
        public void SetUp()
        {
            _executor = new CommandExecutor(
                null,
                new ICommandRegistrationContext[] { new DefaultCommandRegistrationContext() },
                autoTick: false);

            _done = () => _completed++;
            _completed = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _executor.Dispose();
            _prebuiltTree = null;
        }

        [Test, Performance]
        public void ConcurrentTrees_BuildAndRunToCompletion()
        {
            Measure.Method(RunConcurrentTrees)
                .WarmupCount(5)
                .MeasurementCount(20)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void PrebuiltTree_ExecuteConcurrentlyAndRunToCompletion()
        {
            _prebuiltTree = BuildTree().Build();

            Measure.Method(RunPrebuiltTrees)
                .WarmupCount(5)
                .MeasurementCount(20)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void InstantTrees_Execute()
        {
            Measure.Method(RunInstantTrees)
                .WarmupCount(5)
                .MeasurementCount(20)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void EndlessSequentialLoops_Tick()
        {
            StartEndlessLoops(GroupMode.Sequential);
            MeasureTicks();
        }

        [Test, Performance]
        public void EndlessParallelLoops_Tick()
        {
            StartEndlessLoops(GroupMode.Parallel);
            MeasureTicks();
        }

        [Test, Performance]
        public void IdleDeepTrees_Tick()
        {
            StartIdleDeepTrees();
            MeasureTicks();
        }

        [Test]
        public void Tick_DoesNotAllocate()
        {
            StartEndlessLoops(GroupMode.Parallel);
            StartIdleDeepTrees();

            Assert.That(() => Tick(MEASURED_FRAMES), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void BuiltTrees_DoNotAllocate_OnceThePoolsAreWarm()
        {
            // The first pass fills the command, node, runner and processor pools up to the peak of 1000 live trees.
            RunConcurrentTrees();

            Assert.That(RunConcurrentTrees, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void PrebuiltTree_DoesNotAllocate_OnceThePoolsAreWarm()
        {
            _prebuiltTree = BuildTree().Build();
            RunPrebuiltTrees();

            Assert.That(RunPrebuiltTrees, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void InstantTrees_DoNotAllocate_OnceThePoolsAreWarm()
        {
            RunInstantTrees();

            Assert.That(RunInstantTrees, Is.Not.AllocatingGCMemory());
        }

        // Sequential
        //   Parallel [Delay 4f, Delay 8f, Callback]
        //   Delay 4f
        //   Sequential, repeat 2 [Delay 2f, Callback]
        //   Callback (done)
        private CommandBuilder BuildTree()
        {
            return _executor.GetBuilder(GroupMode.Sequential)
                .JoinGroup(GroupMode.Parallel, group => group.JoinDelay(4 * FRAME).JoinDelay(8 * FRAME).JoinCallback(Noop))
                .JoinDelay(4 * FRAME)
                .JoinGroup(GroupMode.Sequential, group => group.JoinDelay(2 * FRAME).JoinCallback(Noop), repeat: 2)
                .JoinCallback(_done);
        }

        private void RunConcurrentTrees()
        {
            _completed = 0;

            for (var i = 0; i < TREE_COUNT; i++)
            {
                BuildTree().Execute();
            }

            TickUntilCompleted(TREE_COUNT);
        }

        private void RunPrebuiltTrees()
        {
            _completed = 0;

            // The caller owns a prebuilt tree: every run reads the same commands and nothing is released.
            for (var i = 0; i < TREE_COUNT; i++)
            {
                _executor.Execute(_prebuiltTree);
            }

            TickUntilCompleted(TREE_COUNT);
        }

        private void RunInstantTrees()
        {
            _completed = 0;

            for (var i = 0; i < INSTANT_TREE_COUNT; i++)
            {
                _executor.GetBuilder(GroupMode.Sequential)
                    .JoinCallback(Noop)
                    .JoinGroup(GroupMode.Parallel, group => group.JoinCallback(Noop).JoinCallback(Noop))
                    .JoinCallback(_done)
                    .Execute();
            }

            if (_completed != INSTANT_TREE_COUNT)
            {
                throw new InvalidOperationException("Instant trees must complete inside Execute");
            }
        }

        private void StartEndlessLoops(GroupMode mode)
        {
            for (var i = 0; i < TREE_COUNT; i++)
            {
                _executor.GetBuilder(GroupMode.Sequential)
                    .JoinGroup(mode, group => group.JoinDelay(FRAME).JoinDelay(2 * FRAME), CommandGroup.REPEAT_FOREVER)
                    .Execute();
            }

            Tick(WARMUP_FRAMES);
        }

        // Sequential > Parallel > {Sequential > Delay, Sequential > Delay}: nothing ever completes.
        private void StartIdleDeepTrees()
        {
            var tree = _executor.GetBuilder(GroupMode.Sequential)
                .JoinGroup(GroupMode.Parallel, parallel => parallel
                    .JoinGroup(GroupMode.Sequential, sequence => sequence.JoinDelay(float.MaxValue))
                    .JoinGroup(GroupMode.Sequential, sequence => sequence.JoinDelay(float.MaxValue)))
                .Build();

            for (var i = 0; i < TREE_COUNT; i++)
            {
                _executor.Execute(tree);
            }

            Tick(WARMUP_FRAMES);
        }

        private void MeasureTicks()
        {
            Measure.Method(() => Tick(MEASURED_FRAMES))
                .WarmupCount(3)
                .MeasurementCount(20)
                .GC()
                .Run();
        }

        private void TickUntilCompleted(int count)
        {
            for (var frame = 0; _completed < count; frame++)
            {
                if (frame == MAX_FRAMES_TO_COMPLETE)
                {
                    throw new InvalidOperationException($"Only {_completed} of {count} trees completed in {frame} frames");
                }

                _executor.Tick(FRAME);
            }
        }

        private void Tick(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                _executor.Tick(FRAME);
            }
        }
    }
}
