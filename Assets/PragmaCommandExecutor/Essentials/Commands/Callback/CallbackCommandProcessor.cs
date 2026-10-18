namespace Pragma.CommandExecutor
{
    public class CallbackCommandProcessor : IStatelessCommandProcessor<CallbackCommand>
    {
        [UnityEngine.Scripting.RequiredMember]
        public CallbackCommandProcessor()
        {
        }

        public void Execute(CallbackCommand command)
        {
            command.Callback?.Invoke();
        }
    }
}
