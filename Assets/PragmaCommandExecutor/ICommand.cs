using System;

namespace Pragma.CommandExecutor
{
    public interface ICommand<TProcessor> : ICommand where TProcessor : ICommandProcessor
    {
        Type ICommand.ProcessorType => typeof(TProcessor);
    }
    
    public interface ICommand
    {
        public Type ProcessorType { get; }
    }
}