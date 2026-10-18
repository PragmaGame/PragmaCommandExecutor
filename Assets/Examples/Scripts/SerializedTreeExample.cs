using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// A command tree stored as serialized data: <see cref="CommandGroup"/> and the commands are <c>[Serializable]</c>
    /// and the children are <c>[SerializeReference]</c>, so every value is editable in the inspector. A lerp command can
    /// take its data from a shared <see cref="LerpVector3PresetConfig"/> asset instead: tick <i>Is Use Preset</i>.
    /// The tree belongs to the component: <c>Execute</c> neither copies nor releases it, and the same instance can be
    /// running several times at once.
    /// </summary>
    public class SerializedTreeExample : Example
    {
        [SerializeField] private CommandGroup _tree = new(GroupMode.Sequential, new List<ICommand>
        {
            new CommandGroup(GroupMode.Parallel, new List<ICommand>
            {
                Lerp<ScaleCommand>(Vector3.one, new Vector3(1.6f, 0.5f, 1.6f), 0.25f),
                Lerp<RotateCommand>(Vector3.zero, new Vector3(0f, 90f, 0f), 0.25f),
            }),
            Lerp<ScaleCommand>(new Vector3(1.6f, 0.5f, 1.6f), Vector3.one, 0.4f),
            new DelayCommand { Duration = 0.2f },
            new LogCommand { Message = "Serialized tree: done" },
        });

        public override string Title => "Serialized tree";
        public override string Description => "Plays a tree defined in the inspector: select the object and tweak durations, values or the order.";

        protected override CommandHandle Run()
        {
            return Executor.Execute(_tree);
        }

        private static TCommand Lerp<TCommand>(Vector3 from, Vector3 to, float duration)
            where TCommand : LerpCommand<Transform, Vector3>, new()
        {
            var command = new TCommand();
            command.Data.From = from;
            command.Data.To = to;
            command.Data.Duration = duration;
            return command;
        }
    }
}
