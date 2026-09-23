using System;
using System.Collections.Generic;

namespace Pragma.CommandExecutor
{
    public interface ICommandRegistrationContext
    {
        public IReadOnlyDictionary<Type, Type> Registrations { get; }
    }
}