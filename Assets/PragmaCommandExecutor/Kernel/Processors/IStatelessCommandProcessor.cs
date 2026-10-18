using System;

namespace Pragma.CommandExecutor
{
    public interface IStatelessCommandProcessor<in TCommand> : ICommandProcessor<TCommand>, IStatelessCommandProcessor
        where TCommand : ICommand
    {
        /// <summary>
        /// Runs <paramref name="command"/> from start to end: the command completes inside this call.
        /// </summary>
        void Execute(TCommand command);

        void IStatelessCommandProcessor.Execute(ICommand command)
        {
            if (command is not TCommand typed)
            {
                throw new ArgumentException($"{GetType()} cannot process {command.GetType()}, it expects {typeof(TCommand)}");
            }

            Execute(typed);
        }
    }

    /// <summary>
    /// A processor that keeps no per-run state and completes every command inside <see cref="Execute"/>. A single
    /// instance serves every run of its command type, concurrent and nested ones included (a callback that executes
    /// another command of the same type re-enters <see cref="Execute"/>), and no run holds it between frames: the
    /// executor needs no runtime node for its commands.
    /// </summary>
    public interface IStatelessCommandProcessor : ICommandProcessor
    {
        void Execute(ICommand command);
    }
}
