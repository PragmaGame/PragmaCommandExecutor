using System.Collections.Generic;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// A node of a <see cref="CommandTreePreset"/>: the data of one command (or group) without its targets. The
    /// targets are looked up by <see cref="CommandTreePresetKey{T}"/> in the arguments passed to <see cref="Build"/>.
    /// </summary>
    public interface ICommandTreePresetNode
    {
        /// <summary>
        /// Creates a new command tree owned by the caller. Never returns or shares the data stored on the node.
        /// </summary>
        public ICommand Build(IReadOnlyDictionary<string, object> args);
    }
}
