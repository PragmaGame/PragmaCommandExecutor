using System;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// An exception thrown by a processor never escapes <c>Execute</c> or the player loop: the run finishes as
    /// <see cref="CommandResult.Faulted"/>, the commands still running are interrupted and the exception is logged.
    /// </summary>
    public class ErrorHandlingExample : Example
    {
        public override string Title => "Error handling";
        public override string Description => "Spins, then a callback throws after 0.5 s: the spin stops and the run is Faulted. The exception in the console is expected.";

        protected override CommandHandle Run()
        {
            return Executor.GetBuilder(GroupMode.Parallel)
                .JoinRotate(Target, Vector3.zero, new Vector3(0f, 0f, 720f), 2f)
                .JoinGroup(GroupMode.Sequential, failing => failing
                    .JoinDelay(0.5f)
                    .JoinCallback(() => throw new InvalidOperationException("Simulated failure inside a command")))
                .Execute();
        }
    }
}
