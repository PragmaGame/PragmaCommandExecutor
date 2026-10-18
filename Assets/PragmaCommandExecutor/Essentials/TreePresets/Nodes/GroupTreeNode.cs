using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Builds a <see cref="CommandGroup"/> of the commands its children build, in order.
    /// </summary>
    [Serializable]
    public abstract class GroupTreeNode : ICommandTreePresetNode
    {
        /// <summary>
        /// How many times the group runs again after the first pass; negative repeats forever. See <see cref="CommandGroup.Repeat"/>.
        /// </summary>
        [field: SerializeField] public int Repeat { get; set; }
        [field: SerializeField, SerializeReference] public List<ICommandTreePresetNode> Children { get; set; } = new();

        protected abstract GroupMode Mode { get; }

        public ICommand Build(IReadOnlyDictionary<string, object> args)
        {
            var group = new CommandGroup(Mode, repeat: Repeat);

            if (Children == null)
            {
                return group;
            }

            for (var i = 0; i < Children.Count; i++)
            {
                var child = Children[i];

                if (child == null)
                {
                    throw new InvalidOperationException($"{GetType().Name} has no node at index {i}");
                }

                group.Commands.Add(child.Build(args));
            }

            return group;
        }
    }

    /// <summary>
    /// Builds a <see cref="CommandGroup"/> that runs its children one after another.
    /// </summary>
    [Serializable]
    public class SequenceTreeNode : GroupTreeNode
    {
        protected override GroupMode Mode => GroupMode.Sequential;
    }

    /// <summary>
    /// Builds a <see cref="CommandGroup"/> that runs its children at the same time.
    /// </summary>
    [Serializable]
    public class ParallelTreeNode : GroupTreeNode
    {
        protected override GroupMode Mode => GroupMode.Parallel;
    }
}
