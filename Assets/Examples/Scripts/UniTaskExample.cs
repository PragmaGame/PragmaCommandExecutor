#if COMMAND_EXECUTOR_UNITASK_SUPPORT
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// UniTask integration: the token overloads of <c>Execute</c> are awaitable, cancelling the token cancels the run,
    /// and a faulted run rethrows its exception from <c>await</c>. A cancelled run completes the await normally,
    /// so check the token after it.
    /// </summary>
    public class UniTaskExample : Example
    {
        private CancellationTokenSource _cancellation;

        public override string Title => "UniTask";
        public override string Description => "Awaits a fake 1 s load (an AsyncCommandProcessor), then awaits a pulse. Cancel works at any point.";

        public override bool IsRunning => _cancellation != null;

        public override void Play()
        {
            Cancel();
            PlayAsync().Forget();
        }

        public override void Cancel()
        {
            _cancellation?.Cancel();
        }

        protected override CommandHandle Run()
        {
            // Not used: this example drives its runs from PlayAsync.
            return default;
        }

        private async UniTaskVoid PlayAsync()
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            _cancellation = cancellation;

            try
            {
                Status = "Loading";
                await Executor.Execute<FakeLoadCommand>(command => command.Seconds = 1f, cancellation.Token);

                if (cancellation.IsCancellationRequested)
                {
                    Status = "Cancelled while loading";
                    return;
                }

                Status = "Loaded, pulsing";
                await Executor.GetBuilder(GroupMode.Sequential)
                    .JoinScale(Target, Vector3.one, Vector3.one * 1.5f, 0.3f)
                    .JoinScale(Target, Vector3.one * 1.5f, Vector3.one, 0.3f)
                    .Execute(cancellation.Token);

                Status = cancellation.IsCancellationRequested ? "Cancelled while pulsing" : "Completed";
            }
            catch (Exception exception)
            {
                // A faulted run rethrows here, and the executor does not log it a second time.
                Status = "Faulted";
                Debug.LogException(exception);
            }
            finally
            {
                if (_cancellation == cancellation)
                {
                    _cancellation = null;
                }
            }
        }
    }
}
#endif
