using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Pragma.CommandExecutor
{
    public class CommandExecutor : ICommandExecutor
    {
        private Dictionary<Type, Stack<ICommandProcessor>> _pool;
        private ICommandProcessorFactory _factory;

        public CommandExecutor(ICommandProcessorFactory factory = null)
        {
            _factory = factory;
            
            if (_factory == null)
            {
                _factory = new CommandProcessorFactory();
            }

            _pool = new Dictionary<Type, Stack<ICommandProcessor>>();
        }

        public UniTask Execute(IEnumerable<ICommand> commands, CancellationToken token, ExecuteFormat format = ExecuteFormat.Parallel)
        {
            return format switch
            {
                ExecuteFormat.Parallel => ParallelExecute(commands, token),
                ExecuteFormat.Sequence => SequenceExecute(commands, token),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Incorrect execution format")
            };
        }
        
        public async UniTask Execute(ICommand command, CancellationToken token)
        {
            var processor = Get(command);
            
            await processor.Execute(command, token);
                
            Release(command.ProcessorType, processor);
        }

        private async UniTask ParallelExecute(IEnumerable<ICommand> commands, CancellationToken token)
        {
            await UniTask.WhenAll(commands.Select(command => Execute(command, token)));
        }

        private async UniTask SequenceExecute(IEnumerable<ICommand> commands, CancellationToken token)
        {
            foreach (var command in commands)
            { 
                await Execute(command, token);
            }
        }

        private ICommandProcessor Get(ICommand command)
        {
            var processorType = command.ProcessorType;
            
            if(_pool.TryGetValue(processorType, out var processors))
            {
                if (processors.TryPop(out var processor))
                {
                    return processor;
                }

                return _factory.Create(processorType);
            }

            _pool.Add(processorType, new Stack<ICommandProcessor>());
            
            return _factory.Create(processorType);
        }

        private void Release(Type processorType, ICommandProcessor processor)
        {
            _pool[processorType].Push(processor);
        }
    }
}
