using System;
using System.Collections.Generic;

namespace Pragma.CommandExecutor
{
    public class DefaultCommandRegistrationContext : ICommandRegistrationContext
    {
        public IReadOnlyDictionary<Type, Type> Registrations { get; }
        
        [UnityEngine.Scripting.RequiredMember]
        public DefaultCommandRegistrationContext()
        {
            Registrations = new Dictionary<Type, Type>
            {
                {typeof(DelayCommand), typeof(DelayCommandProcessor)},
                {typeof(CallbackCommand), typeof(CallbackCommandProcessor)},
                {typeof(LogCommand), typeof(LogCommandProcessor)},
                
                {typeof(ScaleCommand), typeof(ScaleCommandProcessor)},
            };
        }
    }
}