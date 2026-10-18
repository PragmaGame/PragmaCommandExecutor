namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Player loop phase at the beginning of which an auto-ticked <see cref="CommandExecutor"/> is ticked.
    /// Declared in player loop order; <see cref="Update"/> is the default.
    /// </summary>
    public enum CommandUpdatePhase
    {
        EarlyUpdate = 1,

        /// <summary>Ticked once per fixed step with the fixed time step, possibly several times or not at all per frame.</summary>
        FixedUpdate = 2,

        PreUpdate = 3,
        Update = 0,
        PreLateUpdate = 4,
        PostLateUpdate = 5,
    }
}
