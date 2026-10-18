using System;

namespace Pragma.CommandExecutor
{
    public interface IStatefulCommandProcessor<in TCommand> : ICommandProcessor<TCommand>, IStatefulCommandProcessor
        where TCommand : ICommand
    {
        /// <summary>
        /// Begins the run: keeps what the ticks need and performs the one-off actions (reads the starting value, plays a
        /// sound). The first <see cref="IStatefulCommandProcessor.Tick"/> follows right away.
        /// </summary>
        void Start(TCommand command);

        void IStatefulCommandProcessor.Start(ICommand command)
        {
            if (command is not TCommand typed)
            {
                throw new ArgumentException($"{GetType()} cannot process {command.GetType()}, it expects {typeof(TCommand)}");
            }

            Start(typed);
        }
    }

    /// <summary>
    /// A processor that keeps per-run state. An instance is taken from the pool for a single run:
    /// <see cref="Start"/> → <see cref="Tick"/> → <see cref="Tick"/> once per frame while it reports
    /// <see cref="CommandStatus.Running"/> → <see cref="Cleanup"/> → back to the pool.
    /// </summary>
    public interface IStatefulCommandProcessor : ICommandProcessor
    {
        void Start(ICommand command);

        /// <summary>
        /// Advances the run by <paramref name="deltaTime"/> seconds of the executor time. The first call comes right after
        /// <see cref="Start"/>, in the same frame, with <paramref name="deltaTime"/> 0: an instant command completes there.
        /// Then it is called once per frame while the processor reports <see cref="CommandStatus.Running"/>.
        /// </summary>
        CommandStatus Tick(float deltaTime);

        /// <summary>
        /// Called exactly once per run, right before the processor goes back to the pool, also when <see cref="Start"/>
        /// or <see cref="Tick"/> threw. <paramref name="interrupted"/> is true when the run was cancelled or faulted while
        /// this processor was running.
        /// </summary>
        void Cleanup(bool interrupted);
    }
}
