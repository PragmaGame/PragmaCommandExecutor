using System;
using System.Collections.Generic;
using System.Linq;

namespace Pragma.CommandExecutor
{
    public class DefaultCommandProcessorPool : ICommandProcessorPool
    {
        private readonly List<ICommandProcessorFactory> _factories;
        private readonly Dictionary<Type, Queue<ICommandProcessor>> _pool;
        
        public DefaultCommandProcessorPool(IEnumerable<ICommandProcessorFactory> factories)
        {
            _pool = new Dictionary<Type, Queue<ICommandProcessor>>();
            
            _factories = factories != null ? factories.ToList() : new List<ICommandProcessorFactory>();
        }
        
        public void AddFactory(ICommandProcessorFactory factory)
        {
            _factories.Add(factory);
        }

        public ICommandProcessor Get(Type commandType)
        {
            if (_pool.TryGetValue(commandType, out var queue) && queue.TryDequeue(out var processor))
            {
                return processor;
            }

            foreach (var factory in _factories)
            {
                if (factory.TryCreate(commandType, out processor))
                {
                    return processor;
                }
            }
            
            return null;
        }

        public void Release(ICommandProcessor processor)
        {
            var commandType = processor.CommandType;

            if (!_pool.TryGetValue(commandType, out var queue))
            {
                queue = new Queue<ICommandProcessor>();
                _pool[commandType] = queue;
            }

            queue.Enqueue(processor);
        }
    }
}