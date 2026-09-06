using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    public class LogCommandProcessor : ICommandProcessor<LogCommand>
    {
        [UnityEngine.Scripting.RequiredMember]
        LogCommandProcessor()
        {
        }

        public void Shutdown()
        {
            
        }

        public UniTask Execute(LogCommand command, CancellationToken cancellationToken = default)
        {
            Debug.unityLogger.Log(command.LogType, command.Message);
            return UniTask.CompletedTask;
        }
    }
}