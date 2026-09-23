using System;

namespace Pragma.CommandExecutor
{
    public interface ICommandProcessor<in TCommand> : ICommandProcessor where TCommand : ICommand
    {
        /// <summary>
        /// Starts the run and performs its first step: an instant processor does all its work here and returns
        /// <see cref="CommandStatus.Completed"/>. A processor that keeps running can end with <c>return Tick(0f);</c>
        /// to keep the step logic in one place.
        /// </summary>
        CommandStatus Start(TCommand command);

        CommandStatus ICommandProcessor.Start(object command)
        {
            if (command is not TCommand typed)
            {
                throw new ArgumentException($"{GetType()} cannot process {command.GetType()}, it expects {typeof(TCommand)}");
            }

            return Start(typed);
        }

        // Defaults for instant processors: they finish inside Start and hold no per-run state.
        CommandStatus ICommandProcessor.Tick(float deltaTime) => CommandStatus.Completed;

        void ICommandProcessor.Cleanup(bool interrupted)
        {
        }
    }

    /// <summary>
    /// Runs one command instance. A processor is taken from the pool for a single run:
    /// <see cref="Start"/> → <see cref="Tick"/> once per frame while it reports <see cref="CommandStatus.Running"/>
    /// → <see cref="Cleanup"/> → back to the pool.
    /// An <see cref="IStatelessCommandProcessor"/> goes through the same calls, but one instance serves all runs.
    /// </summary>
    public interface ICommandProcessor
    {
        CommandStatus Start(object command);

        /// <summary>
        /// Called once per frame while the processor is running, starting with the frame after <see cref="Start"/>.
        /// </summary>
        CommandStatus Tick(float deltaTime);

        /// <summary>
        /// Called exactly once per run, right before the processor goes back to the pool.
        /// <paramref name="interrupted"/> is true when the run was cancelled or faulted while this processor was running.
        /// </summary>
        void Cleanup(bool interrupted);
    }
}
