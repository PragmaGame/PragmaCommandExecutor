using System;
using System.Collections.Generic;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Processors of one command registration: pooled per-run instances of an <see cref="IStatefulCommandProcessor"/>,
    /// or the single shared instance of an <see cref="IStatelessCommandProcessor"/>.
    /// </summary>
    internal sealed class ProcessorPool
    {
        private readonly Stack<IStatefulCommandProcessor> _instances = new();

        public Type ProcessorType { get; }
        public bool IsStateless { get; }

        /// <summary>
        /// The instance of a stateless registration, created on first use.
        /// </summary>
        public IStatelessCommandProcessor Shared { get; set; }

        public ProcessorPool(Type processorType, bool isStateless)
        {
            ProcessorType = processorType;
            IsStateless = isStateless;
        }

        public bool TryRent(out IStatefulCommandProcessor processor)
        {
            return _instances.TryPop(out processor);
        }

        public void Release(IStatefulCommandProcessor processor)
        {
            _instances.Push(processor);
        }
    }
}
