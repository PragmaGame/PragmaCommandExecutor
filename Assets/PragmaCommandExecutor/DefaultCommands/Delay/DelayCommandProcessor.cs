using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    public class DelayCommandProcessor : ICommandProcessor<DelayCommand>
    {
        [UnityEngine.Scripting.RequiredMember]
        public DelayCommandProcessor()
        {
        }
        
        public void Shutdown()
        {
        }

        public UniTask Execute(DelayCommand command, CancellationToken cancellationToken = default)
        {
            return UniTask.Delay(TimeSpan.FromSeconds(command.Duration), cancellationToken: cancellationToken);
        }
    }
}