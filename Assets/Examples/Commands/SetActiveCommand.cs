using System;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    [Serializable]
    public class SetActiveCommand : ICommand
    {
        [field: SerializeField] public GameObject Target { get; set; }
        [field: SerializeField] public bool IsActive { get; set; }

        public void Reset()
        {
            Target = null;
            IsActive = false;
        }
    }

    /// <summary>
    /// An instant command: all the work happens in <see cref="Execute"/>, and the command completes inside it. It keeps
    /// no per-run state, so <see cref="IStatelessCommandProcessor{TCommand}"/> lets one shared instance serve every run
    /// and the executor needs no runtime node for it.
    /// </summary>
    public class SetActiveCommandProcessor : IStatelessCommandProcessor<SetActiveCommand>
    {
        public void Execute(SetActiveCommand command)
        {
            if (command.Target != null)
            {
                command.Target.SetActive(command.IsActive);
            }
        }
    }
}
