namespace Pragma.CommandExecutor
{
    public class DelayCommandProcessor : ICommandProcessor<DelayCommand>
    {
        private float _remaining;

        [UnityEngine.Scripting.RequiredMember]
        public DelayCommandProcessor()
        {
        }

        public CommandStatus Start(DelayCommand command)
        {
            _remaining = command.Duration;
            return _remaining > 0f ? CommandStatus.Running : CommandStatus.Completed;
        }

        public CommandStatus Tick(float deltaTime)
        {
            _remaining -= deltaTime;
            return _remaining > 0f ? CommandStatus.Running : CommandStatus.Completed;
        }

        public void Shutdown()
        {
            _remaining = 0f;
        }
    }
}