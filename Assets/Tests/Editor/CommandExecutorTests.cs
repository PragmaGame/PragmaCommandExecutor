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
        private const float Frame = 0.25f;

        private CommandExecutor _executor;
        private List<string> _log;
        private List<GameObject> _gameObjects;

        [SetUp]
        public void SetUp()
        {
            _executor = new CommandExecutor(
                null,
                new ICommandRegistrationContext[] { new DefaultCommandRegistrationContext() },
                autoTick: false);

            _executor.AddRegistration<ProbeCommand, ProbeProcessor>();
            _executor.AddRegistration<StatelessProbeCommand, StatelessProbeProcessor>();
            _log = new List<string>();
            _gameObjects = new List<GameObject>();
        }

        [TearDown]
        public void TearDown()
        {
            _executor.Dispose();

            foreach (var gameObject in _gameObjects)
            {
                Object.DestroyImmediate(gameObject);
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
            CollectionAssert.AreEqual(new[] { "a", "start:p", "end:p", "shutdown:p", "b" }, _log);
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
            Assert.AreEqual(CommandOutcome.Completed, result?.Outcome);
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
            var handle = _executor.Execute(Sequence(CommandGroup.RepeatForever, Callback("x")));

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
            var handle = _executor.Execute(Sequence(new DelayCommand { Duration = Frame * 2 }, Callback("done")));

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
        public void StatelessProcessor_IsSharedByAllRuns()
        {
            var processors = new List<ICommandProcessor>();

            var first = _executor.Execute(Parallel(StatelessProbe(processors), StatelessProbe(processors)));
            var second = _executor.Execute(StatelessProbe(processors));

            Assert.AreEqual(3, processors.Count);
            Assert.AreEqual(1, processors.Distinct().Count());

            Tick();
            Assert.IsFalse(first.IsRunning);
            Assert.IsFalse(second.IsRunning);
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

            CollectionAssert.AreEqual(new[] { "start:p", "cancel:p", "shutdown:p" }, _log);
            Assert.AreEqual(CommandOutcome.Cancelled, result?.Outcome);
            Assert.IsFalse(handle.IsRunning);

            Tick(3);
            Assert.IsFalse(_log.Contains("never"));
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
            Assert.AreEqual(CommandOutcome.Cancelled, result?.Outcome);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Exception_FaultsRun_AndCancelsSiblings()
        {
            var boom = Probe("boom", 1);
            boom.ThrowOnTick = true;

            var handle = _executor.Execute(Parallel(Probe("slow", 5), boom));
            CommandResult? result = null;
            handle.OnFinished(r => result = r);

            Tick();

            Assert.AreEqual(CommandOutcome.Faulted, result?.Outcome);
            Assert.IsInstanceOf<InvalidOperationException>(result?.Exception);
            Assert.Contains("cancel:slow", _log);
            Assert.Contains("shutdown:slow", _log);
            Assert.Contains("shutdown:boom", _log);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Exception_WithoutListeners_IsLogged()
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
                    delay.Duration = Frame;
                    pooled = delay;
                })
                .Join(external)
                .Execute();

            Tick();

            Assert.IsFalse(handle.IsRunning);
            CollectionAssert.AreEqual(new[] { "external" }, _log);
            Assert.AreEqual(0f, pooled.Duration);
            Assert.IsNotNull(external.Callback);
            Assert.AreSame(pooled, _executor.GetCommand<DelayCommand>());
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
            Assert.AreSame(pooled, _executor.GetCommand<LogCommand>());
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
            Assert.IsFalse(_log.Contains("cancel:b"));
        }

        [Test]
        public void Dispose_CancelsRunningCommands()
        {
            var handle = _executor.Execute(Probe("p", 5));

            _executor.Dispose();

            Assert.IsFalse(handle.IsRunning);
            Assert.Contains("cancel:p", _log);
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
                _executor.Tick(Frame);
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
            return new ScaleCommand
            {
                Context = transform,
                From = Vector3.zero,
                To = Vector3.one,
                Duration = Frame * 2,
                Curve = curve,
            };
        }

        private Transform CreateTransform()
        {
            var gameObject = new GameObject(TestContext.CurrentContext.Test.Name);
            _gameObjects.Add(gameObject);
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
