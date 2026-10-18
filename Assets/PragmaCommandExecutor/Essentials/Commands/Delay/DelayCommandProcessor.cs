namespace Pragma.CommandExecutor
{
    public class DelayCommandProcessor : IStatefulCommandProcessor<DelayCommand>
    {
        private float _remaining;

        [UnityEngine.Scripting.RequiredMember]
        public DelayCommandProcessor()
        {
        }

        public void Start(DelayCommand command)
        {
            _remaining = command.Duration;
        }

        public CommandStatus Tick(float deltaTime)
        {
            _remaining -= deltaTime;
            return _remaining > 0f ? CommandStatus.Running : CommandStatus.Completed;
        }

        public void Cleanup(bool interrupted)
        {
            _remaining = 0f;
        }
    }
}
