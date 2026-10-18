using System;
using Unity.GraphToolkit.Editor;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// The root of a <see cref="CommandTreeGraph"/>: the node wired to <see cref="INPUT_PORT"/> imports as
    /// <see cref="CommandTreePreset.Root"/>.
    /// </summary>
    [Serializable]
    [Node("", null, "Output")]
    public sealed class RootGraphNode : Node
    {
        public const string INPUT_PORT = "Command";

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort(INPUT_PORT)
                .WithDataType(typeof(Untyped))
                .Build();
        }
    }

    [Serializable, Node("Groups", null, "Sequence")] public class SequenceGraphNode : TreeNodeGraphNode<SequenceTreeNode> { }
    [Serializable, Node("Groups", null, "Parallel")] public class ParallelGraphNode : TreeNodeGraphNode<ParallelTreeNode> { }

    [Serializable, Node("Common", null, "Delay")] public class DelayGraphNode : TreeNodeGraphNode<DelayTreeNode> { }
    [Serializable, Node("Common", null, "Log")] public class LogGraphNode : TreeNodeGraphNode<LogTreeNode> { }
    [Serializable, Node("Common", null, "Callback")] public class CallbackGraphNode : TreeNodeGraphNode<CallbackTreeNode> { }

    [Serializable, Node("Transform", null, "Scale")] public class ScaleGraphNode : TreeNodeGraphNode<ScaleTreeNode> { }
}
