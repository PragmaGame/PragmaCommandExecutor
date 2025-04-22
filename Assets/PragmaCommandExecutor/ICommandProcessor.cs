using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    public interface ICommandProcessor<in TCommand> : ICommandProcessor where TCommand : ICommand
    {
        public UniTask Execute(TCommand command, CancellationToken token);

        UniTask ICommandProcessor.Execute(object command, CancellationToken token)
        {
            if (command is not TCommand convert)
            {
                throw new Exception($"Processor cannot convert {command.GetType()} to target type {typeof(TCommand)}");
            }

            return Execute(convert, token);
        }
    }

    public interface ICommandProcessor
    {
        public UniTask Execute(object command, CancellationToken token);
        public void Interrupt();
    }
}