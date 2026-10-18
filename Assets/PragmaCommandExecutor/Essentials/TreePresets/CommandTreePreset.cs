using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// A command tree stored as an asset, with its targets replaced by string keys. <see cref="Build"/> creates a new
    /// tree from the arguments that map each key to its target. Author it as a <c>.commandtree</c> graph in Graph Toolkit
    /// (Unity 6000.6+), which imports as a <see cref="CommandTreePreset"/>, or edit a <c>.asset</c> preset there with
    /// <i>Open in Graph Toolkit</i>.
    /// </summary>
    [CreateAssetMenu(menuName = "Pragma/Command Executor/Tree Preset", fileName = nameof(CommandTreePreset))]
    public class CommandTreePreset : ScriptableObject
    {
        [SerializeReference] private ICommandTreePresetNode _root;

        public ICommandTreePresetNode Root
        {
            get => _root;
            set => _root = value;
        }

        /// <summary>
        /// Creates a new command tree owned by the caller: build it once per set of targets and execute it as many times
        /// as needed, or hand it to <see cref="ICommandExecutor.ExecuteAndRelease"/>. The preset is never changed.
        /// </summary>
        /// <exception cref="KeyNotFoundException"><paramref name="args"/> has no value for a key of the tree.</exception>
        /// <exception cref="InvalidCastException">A value of <paramref name="args"/> is not of the type its key expects.</exception>
        public ICommand Build(IReadOnlyDictionary<string, object> args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            if (_root == null)
            {
                throw new InvalidOperationException($"Tree preset '{name}' has no root node");
            }

            return _root.Build(args);
        }

        /// <summary>
        /// The keys the tree looks up in the arguments of <see cref="Build"/>, each with the type it expects, in tree
        /// order and without duplicates.
        /// </summary>
        public IReadOnlyList<CommandTreePresetKeyInfo> GetRequiredKeys()
        {
            return CommandTreePresetReflection.CollectKeys(_root);
        }
    }
}
