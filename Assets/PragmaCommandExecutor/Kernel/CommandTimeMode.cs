namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Which frame time the player loop passes to an auto-ticked <see cref="CommandExecutor"/>.
    /// </summary>
    public enum CommandTimeMode
    {
        /// <summary><c>Time.deltaTime</c>: commands stop while <c>Time.timeScale</c> is 0.</summary>
        Scaled = 0,

        /// <summary><c>Time.unscaledDeltaTime</c>: commands keep running during a pause.</summary>
        Unscaled = 1,
    }
}
