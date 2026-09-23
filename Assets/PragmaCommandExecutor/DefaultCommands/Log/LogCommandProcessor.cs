using UnityEngine;

namespace Pragma.CommandExecutor
{
    public class LogCommandProcessor : ICommandProcessor<LogCommand>, IStatelessCommandProcessor
    {
        [UnityEngine.Scripting.RequiredMember]
        public LogCommandProcessor()
        {
        }

        public CommandStatus Start(LogCommand command)
        {
            Debug.unityLogger.Log(command.LogType, command.Message);
            return CommandStatus.Completed;
        }
    }
}