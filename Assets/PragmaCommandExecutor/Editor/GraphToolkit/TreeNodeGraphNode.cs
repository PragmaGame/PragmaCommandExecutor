using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// A node of a <see cref="CommandTreeGraph"/> that imports as an <see cref="ICommandTreePresetNode"/>. Derive
    /// from <see cref="TreeNodeGraphNode{TTreeNode}"/>.
    /// </summary>
    [Serializable]
    public abstract class CommandTreeGraphNode : Node
    {
        public const string OUTPUT_PORT = "Out";

        internal CommandTreeGraphNode()
        {
        }

        /// <summary>The <see cref="ICommandTreePresetNode"/> type this node imports as.</summary>
        public abstract Type TreeNodeType { get; }

        internal TreeNodeSchema Schema => TreeNodeSchema.Get(TreeNodeType);

        /// <summary>The <c>TTreeNode</c> of a <see cref="TreeNodeGraphNode{TTreeNode}"/> type.</summary>
        internal static Type GetTreeNodeType(Type graphNodeType)
        {
            for (var type = graphNodeType; type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TreeNodeGraphNode<>))
                {
                    return type.GetGenericArguments()[0];
                }
            }

            return null;
        }

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            foreach (var port in Schema.Ports)
            {
                if (port.Kind == TreeNodePortKind.ChildList)
                {
                    context.AddOption(port.Id, typeof(int))
                        .WithDisplayName(port.DisplayName)
                        .WithTooltip($"How many nodes {port.DisplayName} has, from 0 to {TreeNodeSchema.MAX_CHILDREN}")
                        .WithDefaultValue(TreeNodeSchema.DEFAULT_CHILDREN)
                        .Delayed()
                        .Build();
                }
            }
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            var schema = Schema;
            var singleList = IsSingleChildList(schema);

            foreach (var port in schema.Ports)
            {
                switch (port.Kind)
                {
                    case TreeNodePortKind.Key:
                        context.AddInputPort(port.Id)
                            .WithDataType(port.DataType)
                            .WithDisplayName(port.DisplayName)
                            .WithTooltip($"Wire a {port.DataType.Name} variable: its name is the key of the target")
                            .Build();
                        break;
                    case TreeNodePortKind.KeyList:
                        context.AddInputPort(port.Id)
                            .WithDataType(port.DataType)
                            .WithDisplayName(port.DisplayName)
                            .WithTooltip($"Wire {port.DataType.Name} variables: their names are the keys of the targets")
                            .WithCapacity(PortCapacity.Multi)
                            .Build();
                        break;
                    case TreeNodePortKind.Child:
                        context.AddInputPort(port.Id)
                            .WithDataType(typeof(Untyped))
                            .WithDisplayName(port.DisplayName)
                            .Build();
                        break;
                    case TreeNodePortKind.ChildList:
                        var count = GetChildCount(port);
                        for (var i = 0; i < count; i++)
                        {
                            context.AddInputPort(port.GetChildPortId(i))
                                .WithDataType(typeof(Untyped))
                                .WithDisplayName(singleList ? i.ToString() : $"{port.DisplayName} {i}")
                                .Build();
                        }

                        break;
                    case TreeNodePortKind.Value:
                        var builder = context.AddInputPort(port.Id)
                            .WithDataType(port.DataType)
                            .WithDisplayName(port.DisplayName);
                        var defaultValue = CopyDefault(port.DefaultValue, port.DataType);
                        if (defaultValue != null)
                        {
                            builder = builder.WithDefaultValue(defaultValue);
                        }

                        builder.Build();
                        break;
                }
            }

            // Links carry no data: an untyped port has no inline value and is not offered as a variable type.
            context.AddOutputPort(OUTPUT_PORT)
                .WithDataType(typeof(Untyped))
                .WithCapacity(PortCapacity.Multi)
                .Build();
        }

        /// <summary>The value of the count option of a child list, clamped to 0..<see cref="TreeNodeSchema.MAX_CHILDREN"/>.</summary>
        internal int GetChildCount(TreeNodePort port)
        {
            var option = GetNodeOptionByName(port.Id);
            if (option == null || !option.TryGetValue(out int count))
            {
                count = TreeNodeSchema.DEFAULT_CHILDREN;
            }

            return Mathf.Clamp(count, 0, TreeNodeSchema.MAX_CHILDREN);
        }

        private static bool IsSingleChildList(TreeNodeSchema schema)
        {
            var lists = 0;
            foreach (var port in schema.Ports)
            {
                if (port.Kind == TreeNodePortKind.Child)
                {
                    return false;
                }

                if (port.Kind == TreeNodePortKind.ChildList)
                {
                    lists++;
                }
            }

            return lists == 1;
        }

        private static object CopyDefault(object value, Type type)
        {
            // The schema keeps one default per type: never hand a mutable one to a port. A port edits its curve or
            // gradient in place and takes no other one, so a field that defaults to null still gets an empty object.
            return value switch
            {
                AnimationCurve curve => new AnimationCurve(curve.keys)
                {
                    preWrapMode = curve.preWrapMode,
                    postWrapMode = curve.postWrapMode,
                },
                Gradient gradient => CopyGradient(gradient),
                null when type == typeof(AnimationCurve) => new AnimationCurve(),
                null when type == typeof(Gradient) => new Gradient(),
                _ => value,
            };
        }

        private static Gradient CopyGradient(Gradient gradient)
        {
            var copy = new Gradient { mode = gradient.mode, colorSpace = gradient.colorSpace };
            copy.SetKeys(gradient.colorKeys, gradient.alphaKeys);
            return copy;
        }
    }

    /// <summary>
    /// The graph node for <typeparamref name="TTreeNode"/>: one port per field Unity serializes on it. Declare a
    /// non-generic <c>[Serializable]</c> subclass, with <c>[UseWithGraph(typeof(CommandTreeGraph))]</c> when it lives
    /// outside this assembly, to list it in the graph.
    /// </summary>
    [Serializable]
    public abstract class TreeNodeGraphNode<TTreeNode> : CommandTreeGraphNode
        where TTreeNode : ICommandTreePresetNode, new()
    {
        public sealed override Type TreeNodeType => typeof(TTreeNode);
    }
}
