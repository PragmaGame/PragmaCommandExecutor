using System;

namespace Pragma.CommandExecutor
{
    public interface ITypedPool<T>
    {
        T Get(Type type);
        void Release(T instance);
        void SetFactory(IObjectFactory factory);

        public TInstance Get<TInstance>() where TInstance : T
        {
            var instance = Get(typeof(TInstance));

            if(instance is not TInstance convert)
            {
                throw new Exception($"Pool cannot convert {instance.GetType()} to target type {typeof(T)}");
            }

            return convert;
        }
    }
}