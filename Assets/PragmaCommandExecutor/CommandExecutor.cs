using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Pool;

namespace Pragma.CommandExecutor
{
    public partial class CommandExecutor : ICommandExecutor
    {
        private ICommandProcessorPool _processorPool;
        
        public CommandExecutor(ICommandProcessorPool processorPool)
        {
            _processorPool = processorPool ?? new DefaultCommandProcessorPool(null);
        }
        
        public void AddFactory(ICommandProcessorFactory factory)
        {
            _processorPool.AddFactory(factory);
        }
        
        private async UniTask ExecuteConcrete(ICommand command, CancellationToken token = default)
        {
            var processor = _processorPool.Get(command.GetType());

            if (processor == null)
            {
                return;
            }
            
            try
            {
                await processor.Execute(command, token).SuppressCancellationThrow();
            }
            finally
            {
                _processorPool.Release(processor);
            }
        }
        
        public async UniTask Execute(List<ICommand> commands, CommandExecuteFormat executeFormat, CancellationToken token = default)
        {
            if (executeFormat == CommandExecuteFormat.Parallel)
            {
                var tasks = ListPool<UniTask>.Get();

                try
                {
                    foreach (var command in commands)
                    {
                        tasks.Add(Execute(command, token));
                    }

                    await UniTask.WhenAll(tasks).SuppressCancellationThrow();
                }
                finally
                {
                    ListPool<UniTask>.Release(tasks);
                }
            }
            else
            {
                foreach (var command in commands)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    var cancelled = await Execute(command, token).SuppressCancellationThrow();

                    if (cancelled)
                    {
                        break;
                    }
                }
            }
        }
        
        public UniTask Execute(ICommand command, CancellationToken token)
        {
            if (command is CommandGroup group)
            {
                return Execute(group, token);
            }

            return ExecuteConcrete(command, token);
        }
    }
}
