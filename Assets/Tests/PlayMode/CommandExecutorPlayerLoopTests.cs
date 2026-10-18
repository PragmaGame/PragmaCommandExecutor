using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace Pragma.CommandExecutor.Tests
{
    /// <summary>
    /// Auto-ticked executors driven by the real player loop, frame by frame. Play mode only: the editor does not run
    /// the player loop in edit mode.
    /// </summary>
    public class CommandExecutorPlayerLoopTests
    {
        private const float TIMEOUT_SECONDS = 5f;

        private readonly List<CommandExecutor> _executors = new();
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _timeScale;

            foreach (var executor in _executors)
            {
                executor.Dispose();
            }

            _executors.Clear();
        }

        [UnityTest]
        public IEnumerator AutoTickedExecutor_IsTickedInItsPhase([Values] CommandUpdatePhase phase)
        {
            Time.timeScale = 1f;
            var executor = Create(new CommandExecutorSettings { UpdatePhase = phase });

            // Inserted at the beginning of the phase; systems installed later (e.g. UniTask's) may precede it.
            var phaseSystem = FindPhase(CommandExecutorPlayerLoop.GetPhaseType(phase));
            Assert.IsTrue(
                Array.Exists(phaseSystem.subSystemList, system => system.type == typeof(CommandExecutorPlayerLoop.CommandExecutorUpdate)),
                $"{phase} has no executor system");

            var handle = executor.Execute(new FramesCommand { Frames = 1 });
            Assert.IsTrue(handle.IsRunning);

            yield return WaitWhileRunning(handle);

            Assert.IsFalse(handle.IsRunning, $"The executor in {phase} was not ticked within {TIMEOUT_SECONDS} s");
        }

        [UnityTest]
        public IEnumerator UnscaledExecutor_KeepsRunningWhileTimeScaleIsZero()
        {
            var scaled = Create(new CommandExecutorSettings { TimeMode = CommandTimeMode.Scaled });
            var unscaled = Create(new CommandExecutorSettings { TimeMode = CommandTimeMode.Unscaled });

            Time.timeScale = 0f;

            var scaledHandle = scaled.Execute(new DelayCommand { Duration = 0.05f });
            var unscaledHandle = unscaled.Execute(new DelayCommand { Duration = 0.05f });

            yield return WaitWhileRunning(unscaledHandle);

            Assert.IsFalse(unscaledHandle.IsRunning, "The unscaled executor must run while the game is paused");
            Assert.IsTrue(scaledHandle.IsRunning, "The scaled executor must pause with Time.timeScale = 0");
        }

        [UnityTest]
        public IEnumerator ManualExecutor_IsTickedOnlyByTick()
        {
            var executor = Create(new CommandExecutorSettings { IsAutoTicked = false });
            var handle = executor.Execute(new FramesCommand { Frames = 1 });

            for (var i = 0; i < 3; i++)
            {
                yield return null;
            }

            Assert.IsTrue(handle.IsRunning);

            executor.Tick(0.1f);

            Assert.IsFalse(handle.IsRunning);
        }

        [UnityTest]
        public IEnumerator DisposedExecutor_IsNoLongerTicked()
        {
            var executor = Create(new CommandExecutorSettings());
            var command = new FramesCommand { Frames = int.MaxValue };
            var handle = executor.Execute(command);

            executor.Dispose();
            yield return null;

            Assert.IsFalse(handle.IsRunning);
            Assert.IsTrue(command.WasInterrupted);
            Assert.Throws<ObjectDisposedException>(() => executor.Execute(new FramesCommand { Frames = 1 }));
        }

        private CommandExecutor Create(CommandExecutorSettings settings)
        {
            var executor = new CommandExecutor(null, new ICommandRegistrationContext[] { new DefaultCommandRegistrationContext() }, settings);
            executor.AddRegistration<FramesCommand, FramesCommandProcessor>();
            _executors.Add(executor);
            return executor;
        }

        private static IEnumerator WaitWhileRunning(CommandHandle handle)
        {
            var deadline = Time.realtimeSinceStartup + TIMEOUT_SECONDS;

            while (handle.IsRunning && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static PlayerLoopSystem FindPhase(Type phaseType)
        {
            foreach (var system in PlayerLoop.GetCurrentPlayerLoop().subSystemList)
            {
                if (system.type == phaseType)
                {
                    return system;
                }
            }

            throw new InvalidOperationException($"{phaseType} is not in the player loop");
        }

        private sealed class FramesCommand : ICommand
        {
            public int Frames { get; set; }
            public bool WasInterrupted { get; set; }

            public void Reset()
            {
                Frames = 0;
                WasInterrupted = false;
            }
        }

        /// <summary>
        /// Completes after <see cref="FramesCommand.Frames"/> ticks after the start frame, whatever their delta time.
        /// </summary>
        private sealed class FramesCommandProcessor : IStatefulCommandProcessor<FramesCommand>
        {
            private FramesCommand _command;
            private int _remaining;
            private bool _isStartTick;

            public void Start(FramesCommand command)
            {
                _command = command;
                _remaining = command.Frames;
                _isStartTick = true;
            }

            public CommandStatus Tick(float deltaTime)
            {
                if (_isStartTick)
                {
                    _isStartTick = false;
                }
                else
                {
                    _remaining--;
                }

                return _remaining > 0 ? CommandStatus.Running : CommandStatus.Completed;
            }

            public void Cleanup(bool interrupted)
            {
                _command.WasInterrupted = interrupted;
                _command = null;
            }
        }
    }
}
