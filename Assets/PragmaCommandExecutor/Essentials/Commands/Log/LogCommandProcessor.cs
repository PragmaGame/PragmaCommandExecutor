using UnityEngine;

namespace Pragma.CommandExecutor
{
    public class LogCommandProcessor : IStatelessCommandProcessor<LogCommand>
    {
        [UnityEngine.Scripting.RequiredMember]
        public LogCommandProcessor()
        {
        }

        public void Execute(LogCommand command)
        {
            Debug.unityLogger.Log(command.LogType, command.Message);
        }
    }
}
