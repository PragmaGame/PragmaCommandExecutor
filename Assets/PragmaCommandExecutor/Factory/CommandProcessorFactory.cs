using System;

namespace Pragma.CommandExecutor
{
    public class CommandProcessorFactory : ICommandProcessorFactory
    {
        public virtual ICommandProcessor Create(Type processorType)
        {
            return Activator.CreateInstance(processorType) as ICommandProcessor;
        }
    }
}