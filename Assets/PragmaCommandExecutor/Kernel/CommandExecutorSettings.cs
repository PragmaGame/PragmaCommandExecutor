using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Settings of a <see cref="CommandExecutor"/>. They are read once by its constructor: changing them afterwards
    /// does not affect an executor that already exists.
    /// </summary>
    [Serializable]
    public sealed class CommandExecutorSettings
    {
        /// <summary>
        /// <c>true</c> — the player loop ticks the executor; <c>false</c> — only <see cref="CommandExecutor.Tick"/> does.
        /// </summary>
        [field: SerializeField] public bool IsAutoTicked { get; set; } = true;

        /// <summary>
        /// Which frame time the player loop passes to an auto-ticked executor.
        /// </summary>
        [field: SerializeField] public CommandTimeMode TimeMode { get; set; } = CommandTimeMode.Scaled;

        /// <summary>
        /// The player loop phase at the beginning of which an auto-ticked executor is ticked.
        /// </summary>
        [field: SerializeField] public CommandUpdatePhase UpdatePhase { get; set; } = CommandUpdatePhase.Update;

        internal CommandExecutorSettings Clone() => (CommandExecutorSettings)MemberwiseClone();
    }
}
