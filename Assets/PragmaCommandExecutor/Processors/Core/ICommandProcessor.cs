using System;

namespace Pragma.CommandExecutor
{
    public interface ICommandProcessor<in TCommand> : ICommandProcessor where TCommand : ICommand
    {
        Type ICommandProcessor.CommandType => typeof(TCommand);

        CommandStatus Start(TCommand command);

        CommandStatus ICommandProcessor.Start(object command)
        {
            if (command is not TCommand convert)
            {
                throw new Exception($"Processor cannot convert {command.GetType()} to target type {typeof(TCommand)}");
            }

            return Start(convert);
        }

        // Defaults for instant processors: they finish inside Start and hold no per-run state.
        CommandStatus ICommandProcessor.Tick(float deltaTime) => CommandStatus.Completed;

        void ICommandProcessor.Cancel()
        {
        }

        void ICommandProcessor.Shutdown()
        {
        }
    }

    /// <summary>
    /// Runs one command instance. A processor is taken from the pool for a single run:
    /// <see cref="Start"/> → <see cref="Tick"/> once per frame while it reports <see cref="CommandStatus.Running"/>
    /// → <see cref="Cancel"/> only if the run is interrupted while running → <see cref="Shutdown"/> → back to the pool.
    /// An <see cref="IStatelessCommandProcessor"/> goes through the same calls, but one instance serves all runs.
    /// </summary>
    public interface ICommandProcessor
    {
        Type CommandType { get; }

        CommandStatus Start(object command);
        CommandStatus Tick(float deltaTime);
        void Cancel();

        /// <summary>
        /// Called once per run, after completion or cancellation, right before the processor returns to the pool.
        /// </summary>
        void Shutdown();
    }
}
