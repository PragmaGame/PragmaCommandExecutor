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
        public CommandHandle Execute(IReadOnlyList<ICommand> commands, GroupMode mode);

        /// <summary>
        /// Starts the command tree and releases it via <see cref="ReleaseCommand"/> once the run finishes
        /// (completed, cancelled or faulted). <paramref name="excluded"/> is copied, the caller may reuse it right away.
        /// </summary>
        public CommandHandle ExecuteAndRelease(ICommand command, HashSet<ICommand> excluded = null);

        /// <summary>
        /// Takes a command from the pool, or creates one if the pool is empty.
        /// Give it back with <see cref="ReleaseCommand"/>, or start it with <see cref="ExecuteAndRelease"/>.
        /// </summary>
        public TCommand RentCommand<TCommand>() where TCommand : ICommand, new();

        /// <summary>
        /// Resets <paramref name="command"/> and returns it to the pool; for a group, its children too,
        /// except those in <paramref name="excluded"/>.
        /// </summary>
        public void ReleaseCommand(ICommand command, HashSet<ICommand> excluded = null);
        public CommandBuilder GetBuilder(GroupMode mode);
    }
}
