namespace Pragma.CommandExecutor
{
    public class CallbackCommandProcessor : ICommandProcessor<CallbackCommand>, IStatelessCommandProcessor
    {
        [UnityEngine.Scripting.RequiredMember]
        public CallbackCommandProcessor()
        {
        }

        public CommandStatus Start(CallbackCommand command)
        {
            command.Callback?.Invoke();
            return CommandStatus.Completed;
        }
    }
}