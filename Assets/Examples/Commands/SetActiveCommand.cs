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
    /// An instant command: all the work happens in <see cref="Start"/>, which returns <see cref="CommandStatus.Completed"/>,
    /// so <c>Tick</c> and <c>Cleanup</c> keep their default implementations. It keeps no per-run state, so
    /// <see cref="IStatelessCommandProcessor"/> lets one shared instance serve every run.
    /// </summary>
    public class SetActiveCommandProcessor : ICommandProcessor<SetActiveCommand>, IStatelessCommandProcessor
    {
        public CommandStatus Start(SetActiveCommand command)
        {
            if (command.Target != null)
            {
                command.Target.SetActive(command.IsActive);
            }

            return CommandStatus.Completed;
        }
    }
}
