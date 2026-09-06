using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    public class CallbackCommandProcessor : ICommandProcessor<CallbackCommand>
    {
        [UnityEngine.Scripting.RequiredMember]
        CallbackCommandProcessor()
        {
        }
        
        
        public void Shutdown()
        {
            
        }

        public UniTask Execute(CallbackCommand command, CancellationToken cancellationToken = default)
        {
            command?.Callback();
            return UniTask.CompletedTask;
        }
    }
}