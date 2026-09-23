namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Marks a processor that keeps no per-run state: a single instance serves every run of its command type,
    /// including concurrent and nested ones, instead of a pooled instance per run. The lifecycle calls stay the same.
    /// </summary>
    public interface IStatelessCommandProcessor : ICommandProcessor
    {
    }
}
