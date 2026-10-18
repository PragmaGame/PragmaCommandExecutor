using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Pragma.CommandExecutor.Tests
{
    public class CommandExecutorTests
    {
        // Exactly representable in binary, so accumulated delays never drift.
        private const float FRAME = 0.25f;

        private CommandExecutor _executor;
        private List<string> _log;
        private List<Object> _objects;

        [SetUp]
        public void SetUp()
        {
            _executor = new CommandExecutor(
                null,
                new ICommandRegistrationContext[] { new DefaultCommandRegistrationContext() },
                new CommandExecutorSettings { IsAutoTicked = false });

            _executor.AddRegistration<ProbeCommand, ProbeProcessor>();
            _executor.AddRegistration<StatelessProbeCommand, StatelessProbeProcessor>();
            _log = new List<string>();
            _objects = new List<Object>();
        }

        [TearDown]
        public void TearDown()
        {
            _executor.Dispose();

            foreach (var unityObject in _objects)
            {
                // Some tests destroy their objects themselves.
                if (unityObject != null)
                {
                    Object.DestroyImmediate(unityObject);
                }
            }
        }

        [Test]
        public void Sequence_RunsInOrder_InstantHeadRunsSynchronously()
        {
            var handle = _executor.Execute(Sequence(Callback("a"), Probe("p", 2), Callback("b")));

            CollectionAssert.AreEqual(new[] { "a", "start:p" }, _log);
            Assert.IsTrue(handle.IsRunning);

            Tick();
            CollectionAssert.AreEqual(new[] { "a", "start:p" }, _log);

            Tick();
            CollectionAssert.AreEqual(new[] { "a", "start:p", "end:p", "cleanup:p", "b" }, _log);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Sequence_NextChildIsNotTickedInItsStartFrame()
        {
            var handle = _executor.Execute(Sequence(Probe("a", 1), Probe("b", 1)));

            Tick();
            Assert.Contains("start:b", _log);
            Assert.IsFalse(_log.Contains("end:b"));
            Assert.IsTrue(handle.IsRunning);

            Tick();
            Assert.Contains("end:b", _log);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Execute_ReturnsCompletedHandle_WhenTreeCompletesSynchronously()
        {
            var handle = _executor.Execute(Sequence(Callback("a"), Parallel(Callback("b"), Callback("c"))));

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, _log);
            Assert.IsFalse(handle.IsRunning);

            CommandResult? result = null;
            handle.OnFinished(r => result = r);
            Assert.AreEqual(CommandResult.Completed, result);
        }

        [Test]
        public void Parallel_StartsAll_AndWaitsForLongest()
        {
            var handle = _executor.Execute(new List<ICommand> { Probe("a", 1), Probe("b", 3) }, GroupMode.Parallel);

            CollectionAssert.AreEqual(new[] { "start:a", "start:b" }, _log);

            Tick();
            Assert.Contains("end:a", _log);
            Assert.IsTrue(handle.IsRunning);

            Tick(2);
            Assert.Contains("end:b", _log);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void NestedGroups_ContinueAfterInnerGroupCompletes()
        {
            var handle = _executor.Execute(Sequence(
                Parallel(Probe("a", 1), Sequence(Probe("b", 1), Probe("c", 1))),
                Callback("after")));

            Tick();
            Assert.IsFalse(_log.Contains("after"));

            Tick();
            Assert.AreEqual("after", _log.Last());
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void SameGroup_CanRunTwiceConcurrently()
        {
            var group = Sequence(Probe("p", 2));

            var first = _executor.Execute(group);
            Tick();
            var second = _executor.Execute(group);
            Tick();

            Assert.IsFalse(first.IsRunning);
            Assert.IsTrue(second.IsRunning);

            Tick();
            Assert.IsFalse(second.IsRunning);
            Assert.AreEqual(2, _log.Count(x => x == "end:p"));
        }

        [Test]
        public void Repeat_RunsGroupRepeatPlusOneTimes()
        {
            var handle = _executor.Execute(Sequence(repeat: 2, Probe("p", 1)));

            Tick(3);

            Assert.AreEqual(3, _log.Count(x => x == "start:p"));
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void RepeatForever_OfInstantCommands_RunsOncePerTick()
        {
            var handle = _executor.Execute(Sequence(CommandGroup.REPEAT_FOREVER, Callback("x")));

            Assert.AreEqual(1, _log.Count);

            Tick(3);
            Assert.AreEqual(4, _log.Count);
            Assert.IsTrue(handle.IsRunning);

            handle.Cancel();
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Delay_CompletesAfterDuration()
        {
            var handle = _executor.Execute(Sequence(new DelayCommand { Duration = FRAME * 2 }, Callback("done")));

            Tick();
            Assert.IsEmpty(_log);

            Tick();
            CollectionAssert.AreEqual(new[] { "done" }, _log);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Scale_LerpsLocalScaleOverDuration()
        {
            var transform = CreateTransform();
            var handle = _executor.Execute(Scale(transform, curve: null));

            AssertApproximately(Vector3.zero, transform.localScale);

            Tick();
            AssertApproximately(Vector3.one * 0.5f, transform.localScale);

            Tick();
            AssertApproximately(Vector3.one, transform.localScale);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Scale_EmptyCurve_IsLinear()
        {
            // Unity deserializes a curve left unset in the inspector as a curve without keys.
            var transform = CreateTransform();
            _executor.Execute(Scale(transform, new AnimationCurve()));

            Tick();
            AssertApproximately(Vector3.one * 0.5f, transform.localScale);
        }

        [Test]
        public void Scale_AppliesCurve()
        {
            var transform = CreateTransform();
            var handle = _executor.Execute(Scale(transform, AnimationCurve.Linear(0f, 1f, 1f, 0f)));

            AssertApproximately(Vector3.one, transform.localScale);

            Tick(2);
            AssertApproximately(Vector3.zero, transform.localScale);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void StatelessProcessor_IsSharedByAllRuns_AndCompletesInsideExecute()
        {
            var processors = new List<ICommandProcessor>();

            var first = _executor.Execute(Parallel(StatelessProbe(processors), StatelessProbe(processors)));
            var second = _executor.Execute(StatelessProbe(processors));

            Assert.AreEqual(3, processors.Count);
            Assert.AreEqual(1, processors.Distinct().Count());
            Assert.IsFalse(first.IsRunning);
            Assert.IsFalse(second.IsRunning);
        }

        [Test]
        public void StatelessProcessor_IsReenteredByNestedRun()
        {
            var processors = new List<ICommandProcessor>();
            var outer = StatelessProbe(processors);
            outer.OnExecute = () => _executor.Execute(StatelessProbe(processors));

            var handle = _executor.Execute(outer);

            Assert.AreEqual(2, processors.Count);
            Assert.AreSame(processors[0], processors[1]);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void StatelessProcessor_Exception_FaultsRun()
        {
            var boom = StatelessProbe(new List<ICommandProcessor>());
            boom.OnExecute = () => throw new InvalidOperationException("stateless boom");

            var handle = _executor.Execute(Sequence(Probe("p", 1), boom, Callback("never")));
            CommandResult? result = null;
            handle.OnFinished(r => result = r);

            LogAssert.Expect(LogType.Exception, new Regex("stateless boom"));
            Tick();

            Assert.AreEqual(CommandResult.Faulted, result);
            Assert.IsFalse(_log.Contains("never"));
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void AddRegistration_ProcessorOfNeitherKind_Throws()
        {
            Assert.Throws<ArgumentException>(() => _executor.AddRegistration<ProbeCommand, ContractlessProcessor>());
        }

        [Test]
        public void AddRegistration_ProcessorOfBothKinds_Throws()
        {
            Assert.Throws<ArgumentException>(() => _executor.AddRegistration<ProbeCommand, AmbiguousProcessor>());
        }

        [Test]
        public void Settings_ChangedAfterConstruction_DoNotAffectTheExecutor()
        {
            var settings = new CommandExecutorSettings { IsAutoTicked = false };
            var executor = new CommandExecutor(null, null, settings);
            executor.AddRegistration<ProbeCommand, ProbeProcessor>();
            settings.IsAutoTicked = true;

            var handle = executor.Execute(Probe("p", 1));

            Assert.DoesNotThrow(() => executor.Tick(FRAME));
            Assert.IsFalse(handle.IsRunning);
            executor.Dispose();
        }

        [Test]
        public void Tick_OnAutoTickedExecutor_Throws()
        {
            var executor = new CommandExecutor(null, null);

            Assert.Throws<InvalidOperationException>(() => executor.Tick(FRAME));
            executor.Dispose();
        }

        [Test]
        public void Tick_AfterDispose_DoesNothing()
        {
            _executor.Dispose();

            Assert.DoesNotThrow(() => _executor.Tick(FRAME));
        }

        [Test]
        public void UnknownUpdatePhase_Throws()
        {
            var settings = new CommandExecutorSettings { UpdatePhase = (CommandUpdatePhase)42 };

            Assert.Throws<ArgumentOutOfRangeException>(() => new CommandExecutor(null, null, settings));
        }

        [Test]
        public void Command_TakesDataFromPreset()
        {
            var transform = CreateTransform();
            var preset = CreatePreset(Vector3.zero, Vector3.one * 2f);
            var command = new ScaleCommand { Context = transform };
            command.UsePreset(preset);

            var handle = _executor.Execute(command);
            Tick(2);

            AssertApproximately(Vector3.one * 2f, transform.localScale);
            Assert.AreSame(preset.Data, command.Data);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Command_UsingMissingPreset_FaultsRun()
        {
            var preset = CreatePreset(Vector3.zero, Vector3.one);
            var command = new ScaleCommand { Context = CreateTransform() };
            command.UsePreset(preset);
            Object.DestroyImmediate(preset);

            LogAssert.Expect(LogType.Exception, new Regex("uses a preset, but none is assigned"));
            var handle = _executor.Execute(command);

            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void ReleaseCommand_ResetsLerpDataInPlace_AndLeavesPresetIntact()
        {
            var preset = CreatePreset(Vector3.one, Vector3.one * 3f);
            var command = _executor.RentCommand<ScaleCommand>();
            var data = command.Data;
            data.To = Vector3.one * 5f;
            command.UsePreset(preset);

            _executor.ReleaseCommand(command);

            Assert.IsFalse(command.IsUsePreset);
            Assert.IsNull(command.Preset);
            Assert.AreSame(data, command.Data);
            AssertApproximately(Vector3.zero, data.To);
            AssertApproximately(Vector3.one * 3f, preset.Data.To);
        }

        [Test]
        public void StatefulProcessor_FirstTickComesInStartFrame_WithZeroDelta()
        {
            var probe = Probe("p", 1);
            probe.Deltas = new List<float>();

            var handle = _executor.Execute(probe);
            CollectionAssert.AreEqual(new[] { 0f }, probe.Deltas);
            Assert.IsTrue(handle.IsRunning);

            Tick();
            CollectionAssert.AreEqual(new[] { 0f, FRAME }, probe.Deltas);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void StatefulProcessor_CompletingOnFirstTick_CompletesInsideExecute()
        {
            var handle = _executor.Execute(Sequence(Probe("p", 0), Callback("after")));

            CollectionAssert.AreEqual(new[] { "start:p", "end:p", "cleanup:p", "after" }, _log);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void StatefulProcessor_IsTakenPerRun_AndReusedAfterFinish()
        {
            var processors = new List<ICommandProcessor>();
            var a = Probe("a", 1);
            var b = Probe("b", 1);
            a.Processors = processors;
            b.Processors = processors;

            _executor.Execute(Parallel(a, b));
            Assert.AreNotSame(processors[0], processors[1]);

            Tick();
            _executor.Execute(a);
            CollectionAssert.Contains(processors.Take(2).ToList(), processors[2]);
        }

        [Test]
        public void Cancel_FromOutside_InterruptsRunningAndSkipsRest()
        {
            var handle = _executor.Execute(Sequence(Probe("p", 5), Callback("never")));
            CommandResult? result = null;
            handle.OnFinished(r => result = r);

            Tick();
            handle.Cancel();

            CollectionAssert.AreEqual(new[] { "start:p", "interrupt:p" }, _log);
            Assert.AreEqual(CommandResult.Cancelled, result);
            Assert.IsFalse(handle.IsRunning);

            Tick(3);
            Assert.IsFalse(_log.Contains("never"));
        }

        [Test]
        public void Cancel_InterruptsOnlyRunningProcessors()
        {
            var handle = _executor.Execute(Sequence(Probe("done", 1), Probe("running", 5)));

            Tick();
            handle.Cancel();

            Assert.Contains("cleanup:done", _log);
            Assert.Contains("interrupt:running", _log);
            Assert.IsFalse(_log.Contains("interrupt:done"));
        }

        [Test]
        public void Cancel_FromInsideTree_StopsBeforeNextCommand()
        {
            var handle = default(CommandHandle);

            handle = _executor.Execute(Sequence(
                Probe("p", 1),
                new CallbackCommand { Callback = () => handle.Cancel() },
                Callback("never"),
                Probe("q", 1)));

            CommandResult? result = null;
            handle.OnFinished(r => result = r);

            Tick();

            Assert.IsFalse(_log.Contains("never"));
            Assert.IsFalse(_log.Contains("start:q"));
            Assert.AreEqual(CommandResult.Cancelled, result);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Exception_FaultsRun_AndInterruptsSiblings()
        {
            var boom = Probe("boom", 1);
            boom.ThrowOnTick = true;

            var handle = _executor.Execute(Parallel(Probe("slow", 5), boom));
            CommandResult? result = null;
            handle.OnFinished(r => result = r);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            Tick();

            Assert.AreEqual(CommandResult.Faulted, result);
            Assert.Contains("interrupt:slow", _log);
            Assert.Contains("interrupt:boom", _log);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Exception_OnStart_IsLogged_AndFinishesRun()
        {
            var boom = Probe("boom", 1);
            boom.ThrowOnStart = true;

            LogAssert.Expect(LogType.Exception, new Regex("boom"));

            var handle = _executor.Execute(boom);

            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Builder_ReleasesPooledCommands_AndKeepsExternalOnes()
        {
            var external = new CallbackCommand { Callback = () => _log.Add("external") };
            DelayCommand pooled = null;

            var handle = _executor.GetBuilder(GroupMode.Sequential)
                .Join<DelayCommand>(delay =>
                {
                    delay.Duration = FRAME;
                    pooled = delay;
                })
                .Join(external)
                .Execute();

            Tick();

            Assert.IsFalse(handle.IsRunning);
            CollectionAssert.AreEqual(new[] { "external" }, _log);
            Assert.AreEqual(0f, pooled.Duration);
            Assert.IsNotNull(external.Callback);
            Assert.AreSame(pooled, _executor.RentCommand<DelayCommand>());
        }

        [Test]
        public void ExecuteWithConfigure_ReleasesCommandOnFinish()
        {
            LogCommand pooled = null;
            LogAssert.Expect(LogType.Log, "configured");

            _executor.Execute<LogCommand>(log =>
            {
                log.Message = "configured";
                pooled = log;
            });

            Assert.AreEqual(string.Empty, pooled.Message);
            Assert.AreSame(pooled, _executor.RentCommand<LogCommand>());
        }

        [Test]
        public void StaleHandle_DoesNotAffectReusedRun()
        {
            var first = _executor.Execute(Probe("a", 1));
            Tick();
            Assert.IsFalse(first.IsRunning);

            var second = _executor.Execute(Probe("b", 1));
            Assert.IsTrue(second.IsRunning);
            Assert.IsFalse(first.IsRunning);

            first.Cancel();
            Assert.IsTrue(second.IsRunning);
            Assert.IsFalse(_log.Contains("interrupt:b"));
        }

        [Test]
        public void Dispose_CancelsRunningCommands()
        {
            var handle = _executor.Execute(Probe("p", 5));

            _executor.Dispose();

            Assert.IsFalse(handle.IsRunning);
            Assert.Contains("interrupt:p", _log);
            Assert.Throws<ObjectDisposedException>(() => _executor.Execute(Probe("q", 1)));
        }

        private static void AssertApproximately(Vector3 expected, Vector3 actual)
        {
            Assert.Less((expected - actual).sqrMagnitude, 1e-8f, $"Expected {expected}, but was {actual}");
        }

        private void Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++)
            {
                _executor.Tick(FRAME);
            }
        }

        private ICommand Callback(string name)
        {
            return new CallbackCommand { Callback = () => _log.Add(name) };
        }

        private ProbeCommand Probe(string name, int frames)
        {
            return new ProbeCommand { Name = name, Frames = frames, Log = _log };
        }

        private static StatelessProbeCommand StatelessProbe(List<ICommandProcessor> processors)
        {
            return new StatelessProbeCommand { Processors = processors };
        }

        private static ScaleCommand Scale(Transform transform, AnimationCurve curve)
        {
            var command = new ScaleCommand { Context = transform };
            command.Data.From = Vector3.zero;
            command.Data.To = Vector3.one;
            command.Data.Duration = FRAME * 2;
            command.Data.Curve = curve;
            return command;
        }

        private LerpVector3PresetConfig CreatePreset(Vector3 from, Vector3 to)
        {
            var preset = ScriptableObject.CreateInstance<LerpVector3PresetConfig>();
            _objects.Add(preset);
            preset.Data.From = from;
            preset.Data.To = to;
            preset.Data.Duration = FRAME * 2;
            return preset;
        }

        private Transform CreateTransform()
        {
            var gameObject = new GameObject(TestContext.CurrentContext.Test.Name);
            _objects.Add(gameObject);
            return gameObject.transform;
        }

        private static CommandGroup Sequence(params ICommand[] commands)
        {
            return Sequence(0, commands);
        }

        private static CommandGroup Sequence(int repeat, params ICommand[] commands)
        {
            return new CommandGroup(GroupMode.Sequential, commands.ToList(), repeat);
        }

        private static CommandGroup Parallel(params ICommand[] commands)
        {
            return new CommandGroup(GroupMode.Parallel, commands.ToList());
        }
    }
}
