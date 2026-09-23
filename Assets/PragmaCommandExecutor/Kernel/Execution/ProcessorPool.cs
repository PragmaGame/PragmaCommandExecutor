using System;
using System.Collections.Generic;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Processors of one command registration: pooled per-run instances,
    /// or the single shared instance of an <see cref="IStatelessCommandProcessor"/>.
    /// </summary>
    internal sealed class ProcessorPool
    {
        private readonly Stack<ICommandProcessor> _instances = new();
        private ICommandProcessor _shared;

        public Type ProcessorType { get; }

        public ProcessorPool(Type processorType)
        {
            ProcessorType = processorType;
        }

        public bool TryRent(out ICommandProcessor processor)
        {
            processor = _shared;
            return processor != null || _instances.TryPop(out processor);
        }

        public void OnCreated(ICommandProcessor processor)
        {
            if (processor is IStatelessCommandProcessor)
            {
                _shared = processor;
            }
        }

        public void Release(ICommandProcessor processor)
        {
            if (!ReferenceEquals(processor, _shared))
            {
                _instances.Push(processor);
            }
        }
    }
}
