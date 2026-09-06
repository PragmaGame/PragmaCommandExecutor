using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    public class LerpCommandProcessor<TCommand, TValue> : ICommandProcessor<TCommand> where TCommand : ICommand
    {
        public void Shutdown()
        {
        }

        public UniTask Execute(TCommand command, CancellationToken cancellationToken = default)
        {
            //TODO Lerp
            return UniTask.CompletedTask;
        }
    }
}