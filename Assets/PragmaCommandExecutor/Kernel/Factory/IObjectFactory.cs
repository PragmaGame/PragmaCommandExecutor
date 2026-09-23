using System;

namespace Pragma.CommandExecutor
{
    public interface IObjectFactory
    {
        public bool TryCreate(Type type, out object instance);
    }
}