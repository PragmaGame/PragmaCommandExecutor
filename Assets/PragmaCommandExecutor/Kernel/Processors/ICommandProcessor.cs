namespace Pragma.CommandExecutor
{
    /// <summary>
    /// What every processor of <typeparamref name="TCommand"/> is. A processor implements it through exactly one of
    /// <see cref="IStatelessCommandProcessor{TCommand}"/> and <see cref="IStatefulCommandProcessor{TCommand}"/>;
    /// registering a processor that implements neither or both throws.
    /// </summary>
    public interface ICommandProcessor<in TCommand> : ICommandProcessor where TCommand : ICommand
    {
    }

    /// <summary>
    /// Runs commands of one type: an <see cref="IStatelessCommandProcessor"/> executes each of them instantly with one
    /// shared instance, an <see cref="IStatefulCommandProcessor"/> is taken from the pool for one run.
    /// </summary>
    public interface ICommandProcessor
    {
    }
}
