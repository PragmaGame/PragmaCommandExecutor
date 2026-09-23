using System;
using System.Collections.Generic;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// Maps every example command to its processor. Pass it to the executor next to
    /// <see cref="DefaultCommandRegistrationContext"/>; a later context overrides an earlier one for the same command.
    /// </summary>
    public class ExampleCommandRegistrationContext : ICommandRegistrationContext
    {
        public IReadOnlyDictionary<Type, Type> Registrations { get; } = new Dictionary<Type, Type>
        {
            { typeof(MoveCommand), typeof(MoveCommandProcessor) },
            { typeof(RotateCommand), typeof(RotateCommandProcessor) },
            { typeof(SetActiveCommand), typeof(SetActiveCommandProcessor) },
            { typeof(WaitUntilCommand), typeof(WaitUntilCommandProcessor) },
#if COMMAND_EXECUTOR_UNITASK_SUPPORT
            { typeof(FakeLoadCommand), typeof(FakeLoadCommandProcessor) },
#endif
        };
    }
}
