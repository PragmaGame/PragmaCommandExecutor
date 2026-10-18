using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Pragma.CommandExecutor.Examples;
using UnityEngine;
#if COMMAND_EXECUTOR_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#endif

namespace Pragma.CommandExecutor.Tests
{
    /// <summary>
    /// Exercises the package inside a built player, where managed code stripping has already run: build it with
    /// <i>Tools → Pragma Command Executor → Build Player Smoke Test</i> (High stripping), run it and read the on-screen
    /// report or the player log (<c>[Smoke]</c> lines). In batch mode the player quits with exit code 0 when every check passed.
    /// Also runs in the Editor play mode, which checks the checks themselves.
    /// </summary>
    public class PlayerSmokeTest : MonoBehaviour
    {
        private const float TIMEOUT_SECONDS = 5f;

        [SerializeField] private Transform _target;
        [SerializeField] private GameObject _toggled;

        // Imported from Examples/Presets/Pulse.commandtree: checks that [SerializeReference] tree nodes survive stripping.
        [SerializeField] private CommandTreePreset _treePreset;

        // Deserialized from the scene: checks that [SerializeReference] children survive stripping.
        [SerializeField] private CommandGroup _serializedTree = new(GroupMode.Sequential, new List<ICommand>
        {
            Lerp<MoveCommand>(Vector3.zero, Vector3.right, 0.05f),
            Lerp<ScaleCommand>(Vector3.one, Vector3.one * 0.5f, 0.05f),
            new DelayCommand { Duration = 0.05f },
            new LogCommand { Message = "[Smoke] serialized tree log command" },
        });

        private readonly StringBuilder _report = new();
        private CommandExecutor _executor;
        private int _passed;
        private int _failed;
        private bool _isFinished;

        private IEnumerator Start()
        {
            _executor = new CommandExecutor(null, new ICommandRegistrationContext[]
            {
                new DefaultCommandRegistrationContext(),
                new ExampleCommandRegistrationContext(),
            });

            _executor.AddRegistration<SmokeCommand, SmokeCommandProcessor>();

            yield return DefaultCommands();
            yield return ExampleCommands();
            yield return GenericRegistration();
            yield return SerializedTree();
            yield return TreePreset();
            yield return Repeat();
            yield return Cancel();
            yield return Fault();
            yield return ObjectFactory();
#if COMMAND_EXECUTOR_UNITASK_SUPPORT
            yield return AwaitRethrowsFault();
            yield return AsyncProcessor();
#endif

            _executor.Dispose();
            _isFinished = true;

            var summary = $"[Smoke] RESULT {_passed}/{_passed + _failed} passed";
            _report.AppendLine(summary);
            Debug.Log(summary);

            if (Application.isBatchMode)
            {
                Application.Quit(_failed == 0 ? 0 : 1);
            }
        }

        private void OnGUI()
        {
            var text = _isFinished ? _report.ToString() : _report + "running...";
            GUI.Label(new Rect(10f, 10f, Screen.width - 20f, Screen.height - 20f), text);
        }

        private IEnumerator DefaultCommands()
        {
            var isCalled = false;
            ResetTarget();

            return Run("Default commands through the builder", () => _executor.GetBuilder(GroupMode.Sequential)
                    .JoinDelay(0.05f)
                    .JoinCallback(() => isCalled = true)
                    .JoinLog("[Smoke] log command", LogType.Log)
                    .JoinScale(_target, Vector3.one, Vector3.one * 2f, 0.05f)
                    .Execute(),
                CommandResult.Completed,
                () => isCalled && Approximately(_target.localScale, Vector3.one * 2f));
        }

        private IEnumerator ExampleCommands()
        {
            var frames = 0;
            ResetTarget();
            _toggled.SetActive(true);

            return Run("Custom processors registered by type", () => _executor.GetBuilder(GroupMode.Sequential)
                    .JoinGroup(GroupMode.Parallel, parallel => parallel
                        .JoinMove(_target, Vector3.zero, Vector3.up, 0.05f)
                        .JoinRotate(_target, Vector3.zero, new Vector3(0f, 90f, 0f), 0.05f))
                    .JoinSetActive(_toggled, false)
                    .JoinWaitUntil(() => ++frames >= 3)
                    .Execute(),
                CommandResult.Completed,
                () => Approximately(_target.localPosition, Vector3.up) && !_toggled.activeSelf && frames >= 3);
        }

        private IEnumerator GenericRegistration()
        {
            return Run("Processor registered with AddRegistration<TCommand, TProcessor>",
                () => _executor.Execute<SmokeCommand>(command => command.Frames = 2),
                CommandResult.Completed);
        }

        private IEnumerator SerializedTree()
        {
            ResetTarget();

            return Run("Serialized tree with [SerializeReference] children",
                () => _executor.Execute(_serializedTree),
                CommandResult.Completed,
                () => _serializedTree.Commands.Count == 4
                      && !_serializedTree.Commands.Contains(null)
                      && Approximately(_target.localPosition, Vector3.right)
                      && Approximately(_target.localScale, Vector3.one * 0.5f));
        }

        private IEnumerator TreePreset()
        {
            var pulses = 0;
            ResetTarget();

            var args = new Dictionary<string, object>
            {
                ["target"] = _target,
                ["done"] = (Action)(() => pulses++),
            };

            // Pulse scales the target up and back twice and calls "done" at the end of each pass.
            return Run("Tree preset imported from a graph", () => _executor.ExecuteAndRelease(_treePreset.Build(args)),
                CommandResult.Completed,
                () => pulses == 2 && Approximately(_target.localScale, Vector3.one));
        }

        private IEnumerator Repeat()
        {
            var calls = 0;

            return Run("Repeat", () => _executor.GetBuilder(GroupMode.Sequential)
                    .JoinGroup(GroupMode.Sequential, group => group.JoinDelay(0.01f).JoinCallback(() => calls++), repeat: 2)
                    .Execute(),
                CommandResult.Completed,
                () => calls == 3);
        }

        private IEnumerator Cancel()
        {
            var handle = _executor.GetBuilder(GroupMode.Sequential)
                .JoinGroup(GroupMode.Sequential, loop => loop.JoinDelay(0.01f), CommandGroup.REPEAT_FOREVER)
                .Execute();

            yield return null;
            yield return null;

            CommandResult? result = null;
            handle.OnFinished(r => result = r);
            handle.Cancel();

            Report("Cancel of an endless loop", result == CommandResult.Cancelled && !handle.IsRunning, $"result {result}");
        }

        private IEnumerator Fault()
        {
            Debug.Log("[Smoke] the next exception is expected");

            return Run("Exception in a processor faults the run", () => _executor.GetBuilder(GroupMode.Sequential)
                    .JoinDelay(0.01f)
                    .JoinCallback(() => throw new InvalidOperationException("[Smoke] expected exception"))
                    .Execute(),
                CommandResult.Faulted);
        }

        private IEnumerator ObjectFactory()
        {
            var scoreBoard = new ScoreBoard();
            var executor = new CommandExecutor(new ScoreObjectFactory(scoreBoard), null);
            executor.AddRegistration<AddScoreCommand, AddScoreCommandProcessor>();

            yield return Run("IObjectFactory creates a processor with dependencies",
                () => executor.GetBuilder(GroupMode.Sequential).JoinAddScore(10).JoinAddScore(20).Execute(),
                CommandResult.Completed,
                () => scoreBoard.Score == 30);

            executor.Dispose();
        }

#if COMMAND_EXECUTOR_UNITASK_SUPPORT
        private IEnumerator AwaitRethrowsFault()
        {
            var task = _executor.GetBuilder(GroupMode.Sequential)
                .JoinDelay(0.01f)
                .JoinCallback(() => throw new InvalidOperationException("[Smoke] expected exception for await"))
                .Execute()
                .ToUniTask();

            yield return WaitFor(() => task.Status != UniTaskStatus.Pending);

            Exception thrown = null;

            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            Report("UniTask await rethrows the fault", thrown is InvalidOperationException, $"thrown {thrown}");
        }

        private IEnumerator AsyncProcessor()
        {
            return Run("AsyncCommandProcessor",
                () => _executor.Execute<FakeLoadCommand>(command => command.Seconds = 0.05f),
                CommandResult.Completed);
        }
#endif

        private IEnumerator Run(string name, Func<CommandHandle> start, CommandResult expected, Func<bool> verify = null)
        {
            CommandResult? result = null;

            try
            {
                start().OnFinished(r => result = r);
            }
            catch (Exception exception)
            {
                Report(name, false, $"threw {exception}");
                yield break;
            }

            yield return WaitFor(() => result != null);

            if (result != expected)
            {
                Report(name, false, $"result {(result?.ToString() ?? "timeout")}, expected {expected}");
                yield break;
            }

            Report(name, verify == null || verify(), "verification failed");
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + TIMEOUT_SECONDS;

            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private void Report(string name, bool isPassed, string failure)
        {
            var line = isPassed ? $"[Smoke] PASS {name}" : $"[Smoke] FAIL {name}: {failure}";

            if (isPassed)
            {
                _passed++;
                Debug.Log(line);
            }
            else
            {
                _failed++;
                Debug.LogError(line);
            }

            _report.AppendLine(line);
        }

        private void ResetTarget()
        {
            _target.localPosition = Vector3.zero;
            _target.localRotation = Quaternion.identity;
            _target.localScale = Vector3.one;
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude < 1e-6f;
        }

        private static TCommand Lerp<TCommand>(Vector3 from, Vector3 to, float duration)
            where TCommand : LerpCommand<Transform, Vector3>, new()
        {
            var command = new TCommand();
            command.Data.From = from;
            command.Data.To = to;
            command.Data.Duration = duration;
            return command;
        }
    }

    /// <summary>
    /// A processor without any stripping annotations, registered through the generic AddRegistration.
    /// </summary>
    public class SmokeCommand : ICommand
    {
        public int Frames { get; set; }

        public void Reset()
        {
            Frames = 0;
        }
    }

    public class SmokeCommandProcessor : IStatefulCommandProcessor<SmokeCommand>
    {
        private int _remaining;

        public void Start(SmokeCommand command)
        {
            _remaining = command.Frames;
        }

        public CommandStatus Tick(float deltaTime)
        {
            return _remaining-- > 0 ? CommandStatus.Running : CommandStatus.Completed;
        }

        public void Cleanup(bool interrupted)
        {
            _remaining = 0;
        }
    }
}
