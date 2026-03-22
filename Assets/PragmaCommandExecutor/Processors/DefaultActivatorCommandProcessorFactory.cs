using System;

namespace Pragma.CommandExecutor
{
    public class DefaultActivatorCommandProcessorFactory : ICommandProcessorFactory
    {
        public bool TryCreate(Type processorType, out ICommandProcessor processor)
        {
            processor = null;
            return false;
        }
    }
}