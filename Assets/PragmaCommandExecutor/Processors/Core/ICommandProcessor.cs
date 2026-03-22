using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    public interface ICommandProcessor<in TCommand> : ICommandProcessor where TCommand : ICommand
    {
        Type ICommandProcessor.CommandType => typeof(TCommand);
        
        UniTask Execute(TCommand command, CancellationToken cancellationToken = default);

        UniTask ICommandProcessor.Execute(object command, CancellationToken cancellationToken)
        {
            if (command is not TCommand convert)
            {
                throw new Exception($"Processor cannot convert {command.GetType()} to target type {typeof(TCommand)}");
            }

            return Execute(convert, cancellationToken);
        }
    }

    public interface ICommandProcessor
    {
        Type CommandType { get; }
        
        UniTask Execute(object command, CancellationToken cancellationToken = default);
        void Shutdown();
    }
}