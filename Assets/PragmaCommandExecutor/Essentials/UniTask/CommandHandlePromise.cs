using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Pooled <see cref="IUniTaskSource"/> completed by a <see cref="CommandHandle"/>.
    /// Cancellation of the run completes the task normally (it is suppressed, as before), a faulted run rethrows.
    /// </summary>
    internal sealed class CommandHandlePromise : IUniTaskSource, ITaskPoolNode<CommandHandlePromise>
    {
        private static TaskPool<CommandHandlePromise> _pool;

        private static readonly Action<object> CancelCallback = state => ((CommandHandlePromise)state)._handle.Cancel();

        private readonly Action<CommandResult> _onFinished;

        private CommandHandlePromise _nextNode;
        private UniTaskCompletionSourceCore<AsyncUnit> _core;
        private CommandHandle _handle;
        private CancellationTokenRegistration _registration;

        public ref CommandHandlePromise NextNode => ref _nextNode;

        static CommandHandlePromise()
        {
            TaskPool.RegisterSizeGetter(typeof(CommandHandlePromise), () => _pool.Size);
        }

        private CommandHandlePromise()
        {
            _onFinished = OnFinished;
        }

        public static UniTask Create(CommandHandle handle, CancellationToken cancellationToken)
        {
            if (!handle.IsRunning)
            {
                return UniTask.CompletedTask;
            }

            if (!_pool.TryPop(out var promise))
            {
                promise = new CommandHandlePromise();
            }

            var token = promise._core.Version;

            promise._handle = handle;
            handle.OnFinished(promise._onFinished);

            if (cancellationToken.CanBeCanceled)
            {
                // Invokes the callback synchronously when the token is already cancelled.
                promise._registration = cancellationToken.RegisterWithoutCaptureExecutionContext(CancelCallback, promise);
            }

            return new UniTask(promise, token);
        }

        public UniTaskStatus GetStatus(short token)
        {
            return _core.GetStatus(token);
        }

        public UniTaskStatus UnsafeGetStatus()
        {
            return _core.UnsafeGetStatus();
        }

        public void OnCompleted(Action<object> continuation, object state, short token)
        {
            _core.OnCompleted(continuation, state, token);
        }

        public void GetResult(short token)
        {
            try
            {
                _core.GetResult(token);
            }
            finally
            {
                TryReturn();
            }
        }

        private void OnFinished(CommandResult result)
        {
            _registration.Dispose();

            if (result.IsFaulted)
            {
                _core.TrySetException(result.Exception);
                return;
            }

            _core.TrySetResult(AsyncUnit.Default);
        }

        private void TryReturn()
        {
            _core.Reset();
            _registration.Dispose();
            _registration = default;
            _handle = default;
            _pool.TryPush(this);
        }
    }
}
