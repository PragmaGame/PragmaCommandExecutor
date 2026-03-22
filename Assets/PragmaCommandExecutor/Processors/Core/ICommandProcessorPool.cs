using System;

namespace Pragma.CommandExecutor
{
    public interface ICommandProcessorPool
    {
        ICommandProcessor Get(Type commandType);
        void Release(ICommandProcessor processor);
        void AddFactory(ICommandProcessorFactory factory);
    }
}