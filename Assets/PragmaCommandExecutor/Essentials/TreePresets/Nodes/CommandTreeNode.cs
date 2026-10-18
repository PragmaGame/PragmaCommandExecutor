using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// A node that builds a <typeparamref name="TCommand"/>: its context is the <see cref="Target"/> looked up in the
    /// arguments, its data a copy of <see cref="Data"/>. Derive a non-generic class to make a node for any
    /// <see cref="Command{TContext, TData}"/>: <c>[Serializable] public class MyTreeNode : CommandTreeNode&lt;MyCommand,
    /// Transform, MyData&gt; { }</c>.
    /// </summary>
    [Serializable]
    public abstract class CommandTreeNode<TCommand, TContext, TData> : ICommandTreePresetNode
        where TCommand : Command<TContext, TData>, new()
    {
        [field: SerializeField] public CommandTreePresetKey<TContext> Target { get; set; }
        [field: SerializeField] public TData Data { get; set; } = CommandData.Create<TData>();

        public ICommand Build(IReadOnlyDictionary<string, object> args)
        {
            var command = new TCommand { Context = Target.Resolve(args) };
            command.SetData(CommandTreePresetReflection.CloneData(Data));
            return command;
        }
    }
}
