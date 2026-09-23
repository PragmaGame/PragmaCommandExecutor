using System;

namespace Pragma.CommandExecutor
{
    public static class CommandExecutorExtensions
    {
        /// <summary>
        /// Takes a pooled <typeparamref name="TCommand"/>, lets <paramref name="configure"/> fill it and starts it.
        /// The command returns to the pool when the run finishes.
        /// </summary>
        public static CommandHandle Execute<TCommand>(this ICommandExecutor executor, Action<TCommand> configure)
            where TCommand : ICommand, new()
        {
            var command = executor.RentCommand<TCommand>();
            configure?.Invoke(command);
            return executor.ExecuteAndRelease(command);
        }
    }
}
