using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// Base of every example: owns an executor for the lifetime of the component and exposes Play / Cancel to
    /// <see cref="ExamplesPanel"/>. A real project usually keeps one executor per scope (game, level, UI screen)
    /// rather than one per component.
    /// </summary>
    public abstract class Example : MonoBehaviour
    {
        [SerializeField] private Transform _target;

        private CommandHandle _handle;
        private System.Action<CommandResult> _onFinished;

        public abstract string Title { get; }
        public abstract string Description { get; }

        public Transform Target => _target;
        public string Status { get; protected set; } = "Idle";
        public virtual bool IsRunning => _handle.IsRunning;

        protected CommandExecutor Executor { get; private set; }

        protected virtual void Awake()
        {
            _onFinished = OnFinished;

            // Ticked from the player loop (at the start of Update) until disposed.
            Executor = new CommandExecutor(CreateFactory(), new ICommandRegistrationContext[]
            {
                new DefaultCommandRegistrationContext(),
                new ExampleCommandRegistrationContext(),
            });
        }

        protected virtual void OnDestroy()
        {
            // Cancels whatever is still running: running processors get Cleanup(interrupted: true).
            Executor.Dispose();
        }

        public virtual void Play()
        {
            // Restarts the example: the previous run, if any, finishes as Cancelled first.
            _handle.Cancel();

            Status = "Running";
            _handle = Run();

            // Invoked right away with Completed when the whole tree already completed inside Run().
            _handle.OnFinished(_onFinished);
        }

        public virtual void Cancel()
        {
            // A no-op for a finished run: the handle is versioned, so a stale one never touches a reused run.
            _handle.Cancel();
        }

        /// <summary>
        /// Extra IMGUI controls drawn under the Play / Cancel buttons.
        /// </summary>
        public virtual void DrawControls()
        {
        }

        protected virtual IObjectFactory CreateFactory()
        {
            // null means the default ActivatorFactory: processors are created with their parameterless constructor.
            return null;
        }

        protected abstract CommandHandle Run();

        protected virtual void OnFinished(CommandResult result)
        {
            Status = result.ToString();
        }
    }
}
