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
        private readonly TreeRunner _runner;
        private readonly int _version;

        internal CommandHandle(TreeRunner runner)
        {
            _runner = runner;
            _version = runner.Version;
        }

        public bool IsRunning => _runner != null && _runner.Version == _version;

        /// <summary>
        /// Interrupts the run. Commands that have not started yet are skipped, running processors receive
        /// <see cref="ICommandProcessor.Cleanup"/> with <c>interrupted = true</c>. When called from inside the run itself
        /// (e.g. from a callback command) the run stops as soon as the current step returns.
        /// </summary>
        public void Cancel()
        {
            if (IsRunning)
            {
                _runner.Cancel();
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
                _runner.AddListener(callback);
                return;
            }

            callback(CommandResult.Completed);
        }

        /// <summary>
        /// Same as <see cref="OnFinished(Action{CommandResult})"/>, but also receives the exception of a faulted run.
        /// The listener takes over reporting it, so the executor no longer logs it: the UniTask integration rethrows it
        /// from <c>await</c> instead.
        /// </summary>
        internal void OnFinished(Action<CommandResult, Exception> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (IsRunning)
            {
                _runner.AddListener(callback);
                return;
            }

            callback(CommandResult.Completed, null);
        }
    }
}
