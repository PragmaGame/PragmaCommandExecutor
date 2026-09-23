namespace Pragma.CommandExecutor
{
    /// <summary>
    /// How a <see cref="CommandGroup"/> runs its children.
    /// </summary>
    public enum GroupMode
    {
        /// <summary>All children start together and are ticked in the same frame, on the main thread.</summary>
        Parallel = 0,

        /// <summary>Each child starts after the previous one completes.</summary>
        Sequential = 1,
    }
}