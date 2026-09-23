#if COMMAND_EXECUTOR_UNITASK_SUPPORT
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    [Serializable]
    public class FakeLoadCommand : ICommand
    {
        [field: SerializeField] public float Seconds { get; set; }

        public void Reset()
        {
            Seconds = 0f;
        }
    }

    /// <summary>
    /// A processor written as an ordinary async method: <see cref="AsyncCommandProcessor{TCommand}"/> polls the task
    /// every frame and cancels the token when the run is interrupted.
    /// </summary>
    public class FakeLoadCommandProcessor : AsyncCommandProcessor<FakeLoadCommand>
    {
        protected override async UniTask Execute(FakeLoadCommand command, CancellationToken cancellationToken)
        {
            // Stands in for real asynchronous work: a web request, an Addressables load...
            await UniTask.Delay(TimeSpan.FromSeconds(command.Seconds), cancellationToken: cancellationToken);
        }
    }
}
#endif
