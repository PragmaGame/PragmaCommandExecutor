using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    public class MonoCommandProcessorFactory : MonoBehaviour, ICommandProcessorFactory
    {
        public virtual ICommandProcessor Create(Type processorType)
        {
            return Activator.CreateInstance(processorType) as ICommandProcessor;
        }
    }
}