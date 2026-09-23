namespace Pragma.CommandExecutor
{
    /// <summary>
    /// How a run finished. A run never throws: when a processor throws, the run finishes as <see cref="Faulted"/>
    /// and the exception is logged, or rethrown from <c>await</c> by the UniTask integration.
    /// </summary>
    public enum CommandResult
    {
        Completed = 0,
        Cancelled = 1,
        Faulted = 2,
    }
}
