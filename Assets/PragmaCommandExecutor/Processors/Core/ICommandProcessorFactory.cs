using System;

namespace Pragma.CommandExecutor
{
    public interface ICommandProcessorFactory
    {
        public bool TryCreate(Type processorType, out ICommandProcessor processor);
        
        public bool TryCreate(ICommand command, out ICommandProcessor processor)
        {
            return TryCreate(command.GetType(), out processor);
        }
    }
}