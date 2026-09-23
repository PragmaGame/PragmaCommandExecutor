using System;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Reference to a run started by <see cref="ICommandExecutor"/>.
    /// <para>
    /// Runs are pooled, so the handle is versioned: once the run finishes the handle stops being
    /// <see cref="IsRunning"/> and every call on it becomes a no-op, even after the run object is reused.
    /// <c>default</c> is a handle to an already finished run (returned when the whole tree completed synchronously).
    /// </para>
    /// </summary>
    public readonly struct CommandHandle
    {
        private readonly CommandExecution _execution;
        private readonly int _version;

        internal CommandHandle(CommandExecution execution)
        {
            _execution = execution;
            _version = execution.Version;
        }

        public bool IsRunning => _execution != null && _execution.Version == _version;

        /// <summary>
        /// Interrupts the run. Commands that have not started yet are skipped, running processors receive
        /// <see cref="ICommandProcessor.Cancel"/>. When called from inside the run itself (e.g. from a callback command)
        /// the run stops as soon as the current step returns.
        /// </summary>
        public void Cancel()
        {
            if (IsRunning)
            {
                _execution.Cancel();
            }
        }

        /// <summary>
        /// Registers a callback invoked once when the run finishes.
        /// If the handle is no longer running the callback is invoked immediately with <see cref="CommandResult.Completed"/>.
        /// </summary>
        public void OnFinished(Action<CommandResult> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (IsRunning)
            {
                _execution.AddListener(callback);
                return;
            }

            callback(CommandResult.Completed);
        }
    }
}
