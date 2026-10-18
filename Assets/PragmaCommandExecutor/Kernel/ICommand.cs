namespace Pragma.CommandExecutor
{
    public interface ICommand
    {
        public void Reset();
    }

    /// <summary>
    /// A command split into what it acts on (<see cref="Context"/>, a target set per use) and how
    /// (<see cref="Data"/>, settings that can be shared through a preset). See <see cref="Command{TContext, TData}"/>.
    /// </summary>
    public interface ICommand<out TContext, out TData> : ICommand
    {
        public TContext Context { get; }
        public TData Data { get; }
    }
}
