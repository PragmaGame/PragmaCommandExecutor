using System;

namespace Pragma.CommandExecutor
{
    public class ActivatorFactory : IObjectFactory
    {
        public bool TryCreate(Type type, out object instance)
        {
            try
            {
                instance = Activator.CreateInstance(type);
                return true;
            }
            catch
            {
                instance = null;
                return false;
            }
        }
    }
}