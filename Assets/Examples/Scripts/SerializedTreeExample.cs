using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// A command tree stored as serialized data: <see cref="CommandGroup"/> and the commands are <c>[Serializable]</c>
    /// and the children are <c>[SerializeReference]</c>, so every value is editable in the inspector.
    /// The tree belongs to the component: <c>Execute</c> neither copies nor releases it, and the same instance can be
    /// running several times at once.
    /// </summary>
    public class SerializedTreeExample : Example
    {
        [SerializeField] private CommandGroup _tree = new(GroupMode.Sequential, new List<ICommand>
        {
            new CommandGroup(GroupMode.Parallel, new List<ICommand>
            {
                new ScaleCommand { From = Vector3.one, To = new Vector3(1.6f, 0.5f, 1.6f), Duration = 0.25f },
                new RotateCommand { From = Vector3.zero, To = new Vector3(0f, 90f, 0f), Duration = 0.25f },
            }),
            new ScaleCommand { From = new Vector3(1.6f, 0.5f, 1.6f), To = Vector3.one, Duration = 0.4f },
            new DelayCommand { Duration = 0.2f },
            new LogCommand { Message = "Serialized tree: done" },
        });

        public override string Title => "Serialized tree";
        public override string Description => "Plays a tree defined in the inspector: select the object and tweak durations, values or the order.";

        protected override CommandHandle Run()
        {
            return Executor.Execute(_tree);
        }
    }
}
