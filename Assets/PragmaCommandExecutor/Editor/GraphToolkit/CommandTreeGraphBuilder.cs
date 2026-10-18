using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Builds a <see cref="CommandTreeGraph"/> from an <see cref="ICommandTreePresetNode"/> tree, the reverse of
    /// <see cref="CommandTreeGraphConverter"/>: a graph node per tree node, a variable per key, inline port values for
    /// the data, and a layout from the Output node leftwards.
    /// </summary>
    internal sealed class CommandTreeGraphBuilder
    {
        private const float COLUMN = 640f;
        private const float GAP = 40f;
        private const float HEADER = 48f;
        private const float ROW = 24f;
        private const float VARIABLE_OFFSET = 200f;
        private const float VARIABLE_ROW = 32f;
        private const float OUTPUT_OFFSET = 360f;

        private static readonly MethodInfo PortTrySetValue = typeof(IPort).GetMethod(nameof(IPort.TrySetValue));
        private static readonly Dictionary<Type, MethodInfo> Setters = new();
        private static readonly Dictionary<Type, PropertyInfo> KeyNames = new();
        private static Dictionary<Type, Type> _graphNodeTypes;

        private readonly Graph _graph;
        private readonly Dictionary<object, CommandTreeGraphNode> _built = new(ReferenceComparer.Instance);
        private readonly Dictionary<string, IVariable> _variables = new();

        private CommandTreeGraphBuilder(Graph graph)
        {
            _graph = graph;
        }

        /// <summary>
        /// What keeps <paramref name="root"/> from being shown in a graph: a node type without a graph node, a field the
        /// graph cannot edit, a key used with two types, too many children, a cycle.
        /// </summary>
        public static List<string> Validate(ICommandTreePresetNode root)
        {
            var errors = new List<string>();
            var keyTypes = new Dictionary<string, Type>();
            Validate(root, errors, keyTypes, new HashSet<object>(ReferenceComparer.Instance));
            return errors;
        }

        /// <summary>Adds the Output node and the tree to an empty graph. Call <see cref="Validate"/> first.</summary>
        public static void Build(Graph graph, ICommandTreePresetNode root)
        {
            var output = new RootGraphNode();
            graph.AddNode(output);
            if (root == null)
            {
                return;
            }

            var builder = new CommandTreeGraphBuilder(graph);
            var placement = builder.AddNode(root);
            graph.Connect(placement.Node.GetOutputPortByName(CommandTreeGraphNode.OUTPUT_PORT),
                output.GetInputPortByName(RootGraphNode.INPUT_PORT));
            builder.Place(placement, 0f, 0f);
            output.Position = new Vector2(OUTPUT_OFFSET, placement.Node.Position.y);
        }

        public static bool TryGetGraphNodeType(Type treeNodeType, out Type graphNodeType)
        {
            if (_graphNodeTypes == null)
            {
                _graphNodeTypes = new Dictionary<Type, Type>();
                foreach (var type in TypeCache.GetTypesDerivedFrom<CommandTreeGraphNode>()
                             .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                             .OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    var nodeType = CommandTreeGraphNode.GetTreeNodeType(type);
                    if (nodeType != null && !_graphNodeTypes.ContainsKey(nodeType))
                    {
                        _graphNodeTypes.Add(nodeType, type);
                    }
                }
            }

            return _graphNodeTypes.TryGetValue(treeNodeType, out graphNodeType);
        }

        private static void Validate(ICommandTreePresetNode node, List<string> errors, Dictionary<string, Type> keyTypes,
            HashSet<object> path)
        {
            if (node == null)
            {
                return;
            }

            var type = node.GetType();
            if (!path.Add(node))
            {
                errors.Add($"{type.Name} contains itself");
                return;
            }

            if (!TryGetGraphNodeType(type, out _))
            {
                errors.Add($"{type.Name} has no graph node: declare a TreeNodeGraphNode<{type.Name}> to edit it in the graph");
            }

            var schema = TreeNodeSchema.Get(type);
            errors.AddRange(schema.Errors);

            foreach (var port in schema.Ports)
            {
                var value = GetValue(node, port.Path);
                switch (port.Kind)
                {
                    case TreeNodePortKind.Key:
                        ValidateKey(GetKeyName(value), port.DataType, errors, keyTypes);
                        break;
                    case TreeNodePortKind.KeyList when value is IList keys:
                        foreach (var key in keys)
                        {
                            ValidateKey(GetKeyName(key), port.DataType, errors, keyTypes);
                        }

                        break;
                    case TreeNodePortKind.Child:
                        Validate(value as ICommandTreePresetNode, errors, keyTypes, path);
                        break;
                    case TreeNodePortKind.ChildList when value is IList children:
                        if (children.Count > TreeNodeSchema.MAX_CHILDREN)
                        {
                            errors.Add($"{type.Name}.{port.DisplayName} has {children.Count} nodes, " +
                                       $"more than the {TreeNodeSchema.MAX_CHILDREN} a graph node takes");
                        }

                        foreach (var child in children)
                        {
                            Validate(child as ICommandTreePresetNode, errors, keyTypes, path);
                        }

                        break;
                }
            }

            path.Remove(node);
        }

        private static void ValidateKey(string name, Type type, List<string> errors, Dictionary<string, Type> keyTypes)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (!keyTypes.TryGetValue(name, out var existing))
            {
                keyTypes.Add(name, type);
            }
            else if (existing != type)
            {
                errors.Add($"Key '{name}' is a {existing.Name} in one node and a {type.Name} in another, " +
                           "but a graph variable has one type");
            }
        }

        private Placement AddNode(ICommandTreePresetNode treeNode)
        {
            // A node referenced twice is one graph node wired into both inputs.
            if (_built.TryGetValue(treeNode, out var shared))
            {
                return new Placement(shared, isShared: true);
            }

            TryGetGraphNodeType(treeNode.GetType(), out var graphNodeType);
            var graphNode = (CommandTreeGraphNode)Activator.CreateInstance(graphNodeType);
            _graph.AddNode(graphNode);
            _built.Add(treeNode, graphNode);

            var placement = new Placement(graphNode, isShared: false);
            var schema = graphNode.Schema;

            // The count options define the child ports, so they go first.
            var hasChildLists = false;
            foreach (var port in schema.Ports)
            {
                if (port.Kind == TreeNodePortKind.ChildList)
                {
                    var count = (GetValue(treeNode, port.Path) as IList)?.Count ?? 0;
                    graphNode.GetNodeOptionByName(port.Id).TrySetValue(count);
                    hasChildLists = true;
                }
            }

            if (hasChildLists)
            {
                graphNode.DefineNode();
            }

            foreach (var port in schema.Ports)
            {
                var value = GetValue(treeNode, port.Path);
                switch (port.Kind)
                {
                    case TreeNodePortKind.Key:
                        AddKey(placement, port, value);
                        break;
                    case TreeNodePortKind.KeyList when value is IList keys:
                        foreach (var key in keys)
                        {
                            AddKey(placement, port, key);
                        }

                        break;
                    case TreeNodePortKind.Child when value is ICommandTreePresetNode child:
                        Link(placement, child, port.Id);
                        break;
                    case TreeNodePortKind.ChildList when value is IList children:
                        for (var i = 0; i < children.Count; i++)
                        {
                            if (children[i] is ICommandTreePresetNode listChild)
                            {
                                Link(placement, listChild, port.GetChildPortId(i));
                            }
                        }

                        break;
                    case TreeNodePortKind.Value:
                        SetPortValue(graphNode.GetInputPortByName(port.Id), port.DataType, value);
                        break;
                }
            }

            return placement;
        }

        private void Link(Placement parent, ICommandTreePresetNode child, string portName)
        {
            var placement = AddNode(child);
            parent.Children.Add(placement);
            _graph.Connect(placement.Node.GetOutputPortByName(CommandTreeGraphNode.OUTPUT_PORT),
                parent.Node.GetInputPortByName(portName));
        }

        private static void AddKey(Placement placement, TreeNodePort port, object key)
        {
            // An empty key stays unwired: the graph reports it until a variable is wired in.
            var name = GetKeyName(key);
            if (!string.IsNullOrEmpty(name))
            {
                placement.Keys.Add(new KeyWire(name, port.DataType, port.Id));
            }
        }

        /// <summary>
        /// Places the subtree with its top at <paramref name="top"/>, children one column to the left and the
        /// variables between, and returns its bottom.
        /// </summary>
        private float Place(Placement placement, float x, float top)
        {
            var node = placement.Node;
            var height = Mathf.Max(HEADER + ROW * (node.InputPortCount + node.NodeOptionCount),
                HEADER + VARIABLE_ROW * placement.Keys.Count);

            var childTop = top;
            var childrenBottom = top;
            var hasChildren = false;
            foreach (var child in placement.Children)
            {
                if (child.IsShared)
                {
                    continue;
                }

                childrenBottom = Place(child, x - COLUMN, childTop);
                childTop = childrenBottom + GAP;
                hasChildren = true;
            }

            var y = hasChildren ? Mathf.Max(top, (top + childrenBottom - height) * 0.5f) : top;
            node.Position = new Vector2(x, y);

            // In key order, so that a list of keys keeps its order.
            for (var i = 0; i < placement.Keys.Count; i++)
            {
                var key = placement.Keys[i];
                var position = new Vector2(x - VARIABLE_OFFSET, y + HEADER + VARIABLE_ROW * i);
                var variableNode = _graph.AddVariableNode(GetVariable(key), position);
                _graph.Connect(variableNode.GetOutputPort(0), node.GetInputPortByName(key.Port));
            }

            return Mathf.Max(y + height, childrenBottom);
        }

        private IVariable GetVariable(KeyWire key)
        {
            if (!_variables.TryGetValue(key.Name, out var variable))
            {
                variable = _graph.CreateVariable(key.Name, key.Type, null, VariableKind.Local);
                _variables.Add(key.Name, variable);
            }

            return variable;
        }

        private static void SetPortValue(IPort port, Type type, object value)
        {
            if (port == null || (value == null && type.IsValueType))
            {
                return;
            }

            if (!Setters.TryGetValue(type, out var setter))
            {
                setter = PortTrySetValue.MakeGenericMethod(type);
                Setters[type] = setter;
            }

            if ((bool)setter.Invoke(port, new[] { value }))
            {
                return;
            }

            // A port keeps its curve or gradient as an object it edits in place, and does not take another one.
            switch (value)
            {
                case AnimationCurve curve when port.TryGetValue(out AnimationCurve portCurve) && portCurve != null:
                    portCurve.keys = curve.keys;
                    portCurve.preWrapMode = curve.preWrapMode;
                    portCurve.postWrapMode = curve.postWrapMode;
                    break;
                case Gradient gradient when port.TryGetValue(out Gradient portGradient) && portGradient != null:
                    portGradient.mode = gradient.mode;
                    portGradient.colorSpace = gradient.colorSpace;
                    portGradient.SetKeys(gradient.colorKeys, gradient.alphaKeys);
                    break;
            }
        }

        private static object GetValue(object target, FieldInfo[] path)
        {
            foreach (var field in path)
            {
                if (target == null)
                {
                    return null;
                }

                target = field.GetValue(target);
            }

            return target;
        }

        private static string GetKeyName(object key)
        {
            if (key == null)
            {
                return null;
            }

            var type = key.GetType();
            if (!KeyNames.TryGetValue(type, out var property))
            {
                property = type.GetProperty(nameof(CommandTreePresetKey<object>.Name));
                KeyNames[type] = property;
            }

            return (string)property?.GetValue(key);
        }

        private sealed class Placement
        {
            public Placement(CommandTreeGraphNode node, bool isShared)
            {
                Node = node;
                IsShared = isShared;
            }

            public CommandTreeGraphNode Node { get; }

            /// <summary>The node was placed where it was reached first.</summary>
            public bool IsShared { get; }

            public List<Placement> Children { get; } = new();
            public List<KeyWire> Keys { get; } = new();
        }

        private readonly struct KeyWire
        {
            public KeyWire(string name, Type type, string port)
            {
                Name = name;
                Type = type;
                Port = port;
            }

            public string Name { get; }
            public Type Type { get; }
            public string Port { get; }
        }
    }

    /// <summary>Compares objects by reference, whatever their <see cref="object.Equals(object)"/> says.</summary>
    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();

        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
