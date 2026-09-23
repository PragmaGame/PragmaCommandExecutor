using System.Collections.Generic;

namespace Pragma.CommandExecutor
{
    public interface ICommandExecutor
    {
        /// <summary>
        /// Starts the command tree. The caller keeps ownership of <paramref name="command"/>.
        /// </summary>
        public CommandHandle Execute(ICommand command);

        /// <summary>
        /// Starts <paramref name="commands"/> as an ad-hoc group. The caller keeps ownership of the list and its commands.
        /// </summary>
        public CommandHandle Execute(IReadOnlyList<ICommand> commands, CommandExecuteFormat executeFormat);

        /// <summary>
        /// Starts the command tree and releases it via <see cref="ReleaseCommand"/> once the run finishes
        /// (completed, cancelled or faulted). <paramref name="excluded"/> is copied, the caller may reuse it right away.
        /// </summary>
        public CommandHandle ExecuteAndRelease(ICommand command, HashSet<ICommand> excluded = null);

        public TCommand GetCommand<TCommand>() where TCommand : ICommand;
        public void ReleaseCommand(ICommand command, HashSet<ICommand> excluded = null);
        public CommandBuilder GetBuilder(CommandExecuteFormat executeFormat);
    }
}
