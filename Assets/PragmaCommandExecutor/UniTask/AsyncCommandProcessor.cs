using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Adapter for processors written against the previous async API: implement <see cref="Execute"/> as before
    /// and the task is polled by the executor tick. Completion is observed on the tick after the task finishes.
    /// </summary>
    public abstract class AsyncCommandProcessor<TCommand> : ICommandProcessor<TCommand> where TCommand : ICommand
    {
        private CancellationTokenSource _cancellation;
        private UniTask _task;

        public CommandStatus Start(TCommand command)
        {
            _cancellation ??= new CancellationTokenSource();
            _task = Execute(command, _cancellation.Token);
            return Poll();
        }

        public CommandStatus Tick(float deltaTime)
        {
            return Poll();
        }

        public void Cancel()
        {
            _cancellation?.Cancel();
        }

        public void Shutdown()
        {
            _task = default;

            // A source that was never cancelled is reused by the next run of this pooled processor.
            if (_cancellation != null && _cancellation.IsCancellationRequested)
            {
                _cancellation.Dispose();
                _cancellation = null;
            }
        }

        protected abstract UniTask Execute(TCommand command, CancellationToken cancellationToken);

        private CommandStatus Poll()
        {
            if (_task.Status == UniTaskStatus.Pending)
            {
                return CommandStatus.Running;
            }

            try
            {
                // Observes the result exactly once: returns pooled sources and rethrows faults.
                _task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _task = default;
            }

            return CommandStatus.Completed;
        }
    }
}
