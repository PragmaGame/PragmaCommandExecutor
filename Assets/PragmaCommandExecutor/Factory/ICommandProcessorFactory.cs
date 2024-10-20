using System;

namespace Pragma.CommandExecutor
{
    public interface ICommandProcessorFactory
    {
        public ICommandProcessor Create(Type processorType);
        
        public virtual ICommandProcessor Create(ICommand command)
        {
            return Create(command.ProcessorType);
        }
    }
}