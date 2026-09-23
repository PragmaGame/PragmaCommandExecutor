using System;
using System.Collections.Generic;

namespace Pragma.CommandExecutor
{
    public class TypedPool<T> : ITypedPool<T>
    {
        private readonly IObjectFactory _defaultFactory;
        private IObjectFactory _factory;
        private readonly Dictionary<Type, Queue<T>> _pool;

        public TypedPool(IObjectFactory factory)
        {
            _pool = new Dictionary<Type, Queue<T>>();
            _defaultFactory = new ActivatorFactory();
            _factory = factory ?? _defaultFactory;
        }

        public void SetFactory(IObjectFactory factory)
        {
            _factory = factory;
        }

        public T Get(Type type)
        {
            if (_pool.TryGetValue(type, out var queue) && queue.TryDequeue(out var processor))
            {
                return processor;
            }

            if (_factory.TryCreate(type, out var instance) || _defaultFactory.TryCreate(type, out instance))
            {
                return (T)instance;
            }

            return default;
        }

        public void Release(T instance)
        {
            var instanceType = instance.GetType();

            if (!_pool.TryGetValue(instanceType, out var queue))
            {
                queue = new Queue<T>();
                _pool[instanceType] = queue;
            }

            queue.Enqueue(instance);
        }
    }
}