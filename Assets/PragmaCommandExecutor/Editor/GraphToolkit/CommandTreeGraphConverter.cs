using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    internal readonly struct CommandTreeGraphIssue
    {
        public CommandTreeGraphIssue(bool isError, string message, INode node)
        {
            IsError = isError;
            Message = message;
            Node = node;
        }

        public bool IsError { get; }
        public string Message { get; }

        /// <summary>The node the issue is about, or <c>null</c> for the whole graph.</summary>
        public INode Node { get; }

        public override string ToString() => Message;
    }

    /// <summary>
    /// Turns a <see cref="CommandTreeGraph"/> into the <see cref="ICommandTreePresetNode"/> tree of a
    /// <see cref="CommandTreePreset"/>, and reports what keeps it from importing. The graph validates with the same pass.
    /// </summary>
    internal sealed class CommandTreeGraphConverter
    {
        private static readonly MethodInfo PortTryGetValue = typeof(IPort).GetMethod(nameof(IPort.TryGetValue));
        private static readonly MethodInfo ConstantTryGetValue = typeof(IConstantNode).GetMethod(nameof(IConstantNode.TryGetValue));
        private static readonly Dictionary<Type, MethodInfo> PortGetters = new();
        private static readonly Dictionary<Type, MethodInfo> ConstantGetters = new();

        private readonly List<CommandTreeGraphIssue> _issues = new();
        // Nodes are compared by ID: the graph may hand out different wrappers for one node.
        private readonly HashSet<Hash128> _path = new();
        private readonly HashSet<Hash128> _reached = new();
        private readonly List<IPort> _connected = new();

        private CommandTreeGraphConverter()
        {
        }

        public IReadOnlyList<CommandTreeGraphIssue> Issues => _issues;
        public ICommandTreePresetNode Root { get; private set; }

        public bool HasErrors
        {
            get
            {
                foreach (var issue in _issues)
                {
                    if (issue.IsError)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static CommandTreeGraphConverter Convert(Graph graph)
        {
            var converter = new CommandTreeGraphConverter();
            converter.Run(graph);
            return converter;
        }

        private void Run(Graph graph)
        {
            RootGraphNode root = null;
            foreach (var node in graph.GetNodes())
            {
                switch (node)
                {
                    case RootGraphNode rootNode when root == null:
                        root = rootNode;
                        break;
                    case RootGraphNode rootNode:
                        Error(rootNode, "The graph has more than one Output node");
                        break;
                    case ISubgraphNode subgraph:
                        Error(subgraph, "Command tree graphs do not support subgraphs");
                        break;
                }
            }

            if (root == null)
            {
                Error(null, "The graph has no Output node: add one and wire the root command to it");
                return;
            }

            _reached.Add(root.ID);
            var input = root.GetInputPortByName(RootGraphNode.INPUT_PORT);
            var source = input?.FirstConnectedPort;
            if (source == null)
            {
                Error(root, "Wire a command node to the Output node");
                return;
            }

            Root = BuildLink(source, root);

            foreach (var node in graph.GetNodes())
            {
                if (node is CommandTreeGraphNode && !_reached.Contains(node.ID))
                {
                    Warning(node, $"{node.Title} is not wired to the Output node and is not imported");
                }
            }
        }

        private ICommandTreePresetNode BuildLink(IPort source, INode parent)
        {
            var node = source.GetNode();
            if (node is not CommandTreeGraphNode graphNode)
            {
                Error(parent, $"Only command nodes can be wired to {parent.Title}");
                return null;
            }

            if (!_path.Add(graphNode.ID))
            {
                Error(graphNode, $"{graphNode.Title} contains itself");
                return null;
            }

            _reached.Add(graphNode.ID);
            var treeNode = BuildNode(graphNode);
            _path.Remove(graphNode.ID);
            return treeNode;
        }

        private ICommandTreePresetNode BuildNode(CommandTreeGraphNode graphNode)
        {
            var schema = graphNode.Schema;
            foreach (var error in schema.Errors)
            {
                Error(graphNode, error);
            }

            object instance;
            try
            {
                instance = Activator.CreateInstance(graphNode.TreeNodeType);
            }
            catch (Exception exception)
            {
                Error(graphNode, $"{graphNode.TreeNodeType.Name} cannot be created: {exception.Message}");
                return null;
            }

            foreach (var port in schema.Ports)
            {
                switch (port.Kind)
                {
                    case TreeNodePortKind.Key:
                        if (TryReadKey(graphNode, graphNode.GetInputPortByName(port.Id), port, out var key))
                        {
                            SetValue(instance, port.Path, CreateKey(port, key));
                        }

                        break;
                    case TreeNodePortKind.KeyList:
                        SetValue(instance, port.Path, ReadKeyList(graphNode, port));
                        break;
                    case TreeNodePortKind.Child:
                        SetValue(instance, port.Path, ReadChild(graphNode, graphNode.GetInputPortByName(port.Id), port));
                        break;
                    case TreeNodePortKind.ChildList:
                        SetValue(instance, port.Path, ReadChildList(graphNode, port));
                        break;
                    case TreeNodePortKind.Value:
                        if (TryReadValue(graphNode, graphNode.GetInputPortByName(port.Id), port, out var value))
                        {
                            SetValue(instance, port.Path, value);
                        }

                        break;
                }
            }

            return (ICommandTreePresetNode)instance;
        }

        private bool TryReadKey(INode node, IPort input, TreeNodePort port, out string key)
        {
            key = null;
            var source = input?.FirstConnectedPort;
            if (source == null)
            {
                Error(node, $"{node.Title}: wire a {port.DataType.Name} variable to {port.DisplayName}");
                return false;
            }

            return TryReadKeySource(node, source, port, out key);
        }

        private bool TryReadKeySource(INode node, IPort source, TreeNodePort port, out string key)
        {
            key = null;
            if (source.GetNode() is not IVariableNode variableNode)
            {
                Error(node, $"{node.Title}: {port.DisplayName} takes a variable, whose name is the key, not a value");
                return false;
            }

            var variable = variableNode.Variable;
            if (string.IsNullOrEmpty(variable.Name))
            {
                Error(node, $"{node.Title}: the variable wired to {port.DisplayName} has no name");
                return false;
            }

            if (!port.DataType.IsAssignableFrom(variable.DataType))
            {
                Error(node, $"{node.Title}: {port.DisplayName} takes a {port.DataType.Name}, " +
                            $"variable '{variable.Name}' is a {variable.DataType?.Name}");
                return false;
            }

            key = variable.Name;
            return true;
        }

        private object ReadKeyList(INode node, TreeNodePort port)
        {
            var keyType = typeof(CommandTreePresetKey<>).MakeGenericType(port.DataType);
            var keys = new List<object>();

            _connected.Clear();
            node.GetInputPortByName(port.Id)?.GetConnectedPorts(_connected);
            foreach (var source in _connected)
            {
                if (TryReadKeySource(node, source, port, out var key))
                {
                    keys.Add(Activator.CreateInstance(keyType, key));
                }
            }

            return CreateList(port.FieldType, keyType, keys);
        }

        private ICommandTreePresetNode ReadChild(INode node, IPort input, TreeNodePort port)
        {
            var source = input?.FirstConnectedPort;
            if (source == null)
            {
                Error(node, $"{node.Title}: wire a command node to {DisplayName(input, port)}");
                return null;
            }

            var child = BuildLink(source, node);
            if (child != null && port.Kind == TreeNodePortKind.Child && !port.FieldType.IsInstanceOfType(child))
            {
                Error(node, $"{node.Title}: {port.DisplayName} takes a {port.FieldType.Name}, not {child.GetType().Name}");
                return null;
            }

            return child;
        }

        private object ReadChildList(CommandTreeGraphNode node, TreeNodePort port)
        {
            TreeNodeSchema.TryGetListElementType(port.FieldType, out var elementType);
            var children = new List<object>();

            var count = node.GetChildCount(port);
            for (var i = 0; i < count; i++)
            {
                var child = ReadChild(node, node.GetInputPortByName(port.GetChildPortId(i)), port);
                if (child != null && !elementType.IsInstanceOfType(child))
                {
                    Error(node, $"{node.Title}: {port.DisplayName} takes {elementType.Name} nodes, not {child.GetType().Name}");
                    continue;
                }

                children.Add(child);
            }

            return CreateList(port.FieldType, elementType, children);
        }

        private bool TryReadValue(INode node, IPort input, TreeNodePort port, out object value)
        {
            value = null;
            if (input == null)
            {
                return false;
            }

            var source = input.FirstConnectedPort;
            if (source == null)
            {
                return TryGetValue(PortGetters, PortTryGetValue, input, port.DataType, out value);
            }

            switch (source.GetNode())
            {
                case IConstantNode constant:
                    return TryGetValue(ConstantGetters, ConstantTryGetValue, constant, port.DataType, out value);
                case IVariableNode:
                    Error(node, $"{node.Title}: {port.DisplayName} takes a value or a constant; variables are keys of targets");
                    return false;
                default:
                    Error(node, $"{node.Title}: {port.DisplayName} takes a value or a constant");
                    return false;
            }
        }

        private static bool TryGetValue(Dictionary<Type, MethodInfo> cache, MethodInfo definition, object target, Type type,
            out object value)
        {
            if (!cache.TryGetValue(type, out var method))
            {
                method = definition.MakeGenericMethod(type);
                cache[type] = method;
            }

            var arguments = new object[1];
            var found = (bool)method.Invoke(target, arguments);
            value = arguments[0];
            return found;
        }

        private static object CreateKey(TreeNodePort port, string key)
        {
            return Activator.CreateInstance(port.FieldType, key);
        }

        private static object CreateList(Type listType, Type elementType, List<object> items)
        {
            if (listType.IsArray)
            {
                var array = Array.CreateInstance(elementType, items.Count);
                for (var i = 0; i < items.Count; i++)
                {
                    array.SetValue(items[i], i);
                }

                return array;
            }

            var list = (IList)Activator.CreateInstance(listType);
            foreach (var item in items)
            {
                list.Add(item);
            }

            return list;
        }

        /// <summary>
        /// Sets the field at the end of <paramref name="path"/>, creating the objects on the way and writing structs back.
        /// </summary>
        private static void SetValue(object target, FieldInfo[] path, object value, int index = 0)
        {
            var field = path[index];
            if (index == path.Length - 1)
            {
                field.SetValue(target, value);
                return;
            }

            var child = field.GetValue(target) ?? Activator.CreateInstance(field.FieldType);
            SetValue(child, path, value, index + 1);
            field.SetValue(target, child);
        }

        private static string DisplayName(IPort input, TreeNodePort port)
        {
            return string.IsNullOrEmpty(input?.DisplayName) ? port.DisplayName : input.DisplayName;
        }

        private void Error(INode node, string message) => _issues.Add(new CommandTreeGraphIssue(true, message, node));
        private void Warning(INode node, string message) => _issues.Add(new CommandTreeGraphIssue(false, message, node));
    }
}
