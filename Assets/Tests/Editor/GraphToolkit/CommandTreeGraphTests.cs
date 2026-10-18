using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Pragma.CommandExecutor.Tests
{
    public class CommandTreeGraphTests
    {
        private const string FOLDER = "Assets/CommandTreeGraphTests";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(FOLDER))
            {
                AssetDatabase.CreateFolder("Assets", "CommandTreeGraphTests");
            }
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(FOLDER);
        }

        [Test]
        public void EveryPackageTreeNode_HasAGraphNode()
        {
            var graphNodeTypes = TypeCache.GetTypesDerivedFrom<CommandTreeGraphNode>()
                .Where(t => !t.IsAbstract)
                .Select(CommandTreeGraphNode.GetTreeNodeType)
                .ToList();
            var treeNodeTypes = typeof(ICommandTreePresetNode).Assembly.GetTypes()
                .Where(t => typeof(ICommandTreePresetNode).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            CollectionAssert.IsSubsetOf(treeNodeTypes, graphNodeTypes);
        }

        [Test]
        public void EveryGraphNode_EditsEverySerializedField()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<CommandTreeGraphNode>().Where(t => !t.IsAbstract))
            {
                var schema = TreeNodeSchema.Get(CommandTreeGraphNode.GetTreeNodeType(type));
                CollectionAssert.IsEmpty(schema.Errors, type.Name);
                CollectionAssert.IsNotEmpty(schema.Ports, type.Name);
            }
        }

        [Test]
        public void Schema_FlattensNestedDataIntoNamedPorts()
        {
            var ports = TreeNodeSchema.Get(typeof(ScaleTreeNode)).Ports;

            CollectionAssert.AreEqual(
                new[] { "Target", "Data_From", "Data_To", "Data_Duration", "Data_Curve" },
                ports.Select(p => p.Id));
            Assert.AreEqual(TreeNodePortKind.Key, ports[0].Kind);
            Assert.AreEqual(typeof(Transform), ports[0].DataType);
            Assert.AreEqual("From", ports[1].DisplayName);
        }

        [Test]
        public void Import_Graph_ImportsTheTreeAsACommandTreePreset()
        {
            const string path = FOLDER + "/Import." + CommandTreeGraph.EXTENSION;
            var graph = GraphDatabase.CreateGraph<CommandTreeGraph>(path);

            var output = Add<RootGraphNode>(graph);
            var sequence = Add<SequenceGraphNode>(graph);
            SetChildCount(sequence, 4);
            var scale = Add<ScaleGraphNode>(graph);
            var delay = Add<DelayGraphNode>(graph);
            var log = Add<LogGraphNode>(graph);
            var callback = Add<CallbackGraphNode>(graph);

            Connect(graph, sequence, output, RootGraphNode.INPUT_PORT);
            Connect(graph, scale, sequence, "Children_0");
            Connect(graph, delay, sequence, "Children_1");
            Connect(graph, log, sequence, "Children_2");
            Connect(graph, callback, sequence, "Children_3");

            ConnectVariable<Transform>(graph, "hero", scale, "Target");
            ConnectVariable<Action>(graph, "done", callback, "Callback");
            Assert.IsTrue(sequence.GetInputPortByName("Repeat").TrySetValue(2));
            Assert.IsTrue(scale.GetInputPortByName("Data_To").TrySetValue(new Vector3(1f, 2f, 3f)));
            Assert.IsTrue(delay.GetInputPortByName("Duration").TrySetValue(0.5f));
            var constant = graph.CreateConstantNode(Vector2.zero, "hello");
            Assert.IsTrue(graph.Connect(constant.GetOutputPort(0), log.GetInputPortByName("Message")));

            GraphDatabase.SaveGraph(graph);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var preset = AssetDatabase.LoadAssetAtPath<CommandTreePreset>(path);

            Assert.IsNotNull(preset);
            Assert.AreEqual("Import", preset.name);
            var root = (SequenceTreeNode)preset.Root;
            Assert.AreEqual(2, root.Repeat);
            CollectionAssert.AreEqual(
                new[] { typeof(ScaleTreeNode), typeof(DelayTreeNode), typeof(LogTreeNode), typeof(CallbackTreeNode) },
                root.Children.Select(c => c.GetType()));
            var scaleNode = (ScaleTreeNode)root.Children[0];
            Assert.AreEqual("hero", scaleNode.Target.Name);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), scaleNode.Data.To);
            Assert.AreEqual(0.5f, ((DelayTreeNode)root.Children[1]).Duration);
            Assert.AreEqual("hello", ((LogTreeNode)root.Children[2]).Message);
            Assert.AreEqual("done", ((CallbackTreeNode)root.Children[3]).Callback.Name);
            CollectionAssert.AreEqual(
                new[]
                {
                    new CommandTreePresetKeyInfo("hero", typeof(Transform)),
                    new CommandTreePresetKeyInfo("done", typeof(Action)),
                },
                preset.GetRequiredKeys());
        }

        [Test]
        public void Convert_VariablesOfAnyType_AreKeys()
        {
            var graph = CreateGraph("AnyType");
            var output = Add<RootGraphNode>(graph);
            var parallel = Add<ParallelGraphNode>(graph);
            SetChildCount(parallel, 2);
            var scale = Add<ScaleGraphNode>(graph);
            var callback = Add<CallbackGraphNode>(graph);

            Connect(graph, parallel, output, RootGraphNode.INPUT_PORT);
            Connect(graph, scale, parallel, "Children_0");
            Connect(graph, callback, parallel, "Children_1");
            ConnectVariable<Transform>(graph, "hero", scale, "Target");
            ConnectVariable<Action>(graph, "done", callback, "Callback");

            var converter = CommandTreeGraphConverter.Convert(graph);

            CollectionAssert.IsEmpty(converter.Issues);
            var root = (ParallelTreeNode)converter.Root;
            Assert.AreEqual("hero", ((ScaleTreeNode)root.Children[0]).Target.Name);
            Assert.AreEqual("done", ((CallbackTreeNode)root.Children[1]).Callback.Name);
        }

        [Test]
        public void Convert_WithoutOutput_ReportsAnError()
        {
            var graph = CreateGraph("NoOutput");
            Add<DelayGraphNode>(graph);

            AssertError(CommandTreeGraphConverter.Convert(graph), "no Output node");
        }

        [Test]
        public void Convert_TwoOutputs_ReportsAnError()
        {
            var graph = CreateGraph("TwoOutputs");
            Connect(graph, Add<DelayGraphNode>(graph), Add<RootGraphNode>(graph), RootGraphNode.INPUT_PORT);
            Add<RootGraphNode>(graph);

            AssertError(CommandTreeGraphConverter.Convert(graph), "more than one Output");
        }

        [Test]
        public void Convert_UnwiredKey_ReportsAnError()
        {
            var graph = CreateGraph("UnwiredKey");
            Connect(graph, Add<ScaleGraphNode>(graph), Add<RootGraphNode>(graph), RootGraphNode.INPUT_PORT);

            AssertError(CommandTreeGraphConverter.Convert(graph), "variable to Target");
        }

        [Test]
        public void Convert_EmptyChildPort_ReportsAnError()
        {
            var graph = CreateGraph("EmptyChild");
            var sequence = Add<SequenceGraphNode>(graph);
            SetChildCount(sequence, 2);
            Connect(graph, sequence, Add<RootGraphNode>(graph), RootGraphNode.INPUT_PORT);
            Connect(graph, Add<DelayGraphNode>(graph), sequence, "Children_0");

            AssertError(CommandTreeGraphConverter.Convert(graph), "wire a command node to 1");
        }

        [Test]
        public void Convert_UnreachableNode_IsAWarningOnly()
        {
            var graph = CreateGraph("Unreachable");
            Connect(graph, Add<DelayGraphNode>(graph), Add<RootGraphNode>(graph), RootGraphNode.INPUT_PORT);
            Add<LogGraphNode>(graph);

            var converter = CommandTreeGraphConverter.Convert(graph);

            Assert.IsFalse(converter.HasErrors);
            Assert.IsInstanceOf<DelayTreeNode>(converter.Root);
            Assert.IsTrue(converter.Issues.Any(i => !i.IsError && i.Message.Contains("not wired")));
        }

        [Test]
        public void KeyPort_RejectsConstants_AndDataPort_RejectsVariables()
        {
            var graph = CreateGraph("Rules");
            var scale = Add<ScaleGraphNode>(graph);
            var constant = graph.CreateConstantNode<Transform>(Vector2.zero, null);
            var variable = graph.AddVariableNode(graph.CreateVariable<float>("duration", 1f, VariableKind.Local), Vector2.zero);

            Assert.IsFalse(graph.IsConnectionAllowed(constant.GetOutputPort(0), scale.GetInputPortByName("Target")));
            Assert.IsFalse(graph.IsConnectionAllowed(variable.GetOutputPort(0), scale.GetInputPortByName("Data_Duration")));

            // Graph.Connect applies the same rules.
            Assert.Throws<ArgumentException>(() => graph.Connect(variable.GetOutputPort(0), scale.GetInputPortByName("Data_Duration")));
            Assert.Throws<ArgumentException>(() => graph.Connect(constant.GetOutputPort(0), scale.GetInputPortByName("Target")));
        }

        [Test]
        public void LinkPorts_TakeOnlyTheOutOfCommandNodes()
        {
            var graph = CreateGraph("Links");
            var sequence = Add<SequenceGraphNode>(graph);
            var scale = Add<ScaleGraphNode>(graph);
            var delay = Add<DelayGraphNode>(graph);
            var output = Add<RootGraphNode>(graph);
            var transform = graph.AddVariableNode(graph.CreateVariable("hero", typeof(Transform), null, VariableKind.Local), Vector2.zero);
            var untyped = graph.AddVariableNode(graph.CreateVariable("link", typeof(Untyped), null, VariableKind.Local), Vector2.zero);
            var scaleOut = scale.GetOutputPortByName(CommandTreeGraphNode.OUTPUT_PORT);

            Assert.IsTrue(graph.IsConnectionAllowed(scaleOut, sequence.GetInputPortByName("Children_0")));
            Assert.IsTrue(graph.IsConnectionAllowed(scaleOut, output.GetInputPortByName(RootGraphNode.INPUT_PORT)));
            Assert.IsFalse(graph.IsConnectionAllowed(transform.GetOutputPort(0), sequence.GetInputPortByName("Children_0")));
            Assert.IsFalse(graph.IsConnectionAllowed(untyped.GetOutputPort(0), sequence.GetInputPortByName("Children_0")));
            Assert.IsFalse(graph.IsConnectionAllowed(untyped.GetOutputPort(0), output.GetInputPortByName(RootGraphNode.INPUT_PORT)));
            Assert.IsFalse(graph.IsConnectionAllowed(scaleOut, delay.GetInputPortByName("Duration")));
            Assert.IsFalse(graph.IsConnectionAllowed(scaleOut, Add<ScaleGraphNode>(graph).GetInputPortByName("Target")));
        }

        [Test]
        public void Convert_Cycle_ReportsAnError()
        {
            var graph = CreateGraph("Cycle");
            var first = Add<SequenceGraphNode>(graph);
            var second = Add<SequenceGraphNode>(graph);
            SetChildCount(first, 1);
            SetChildCount(second, 1);
            Connect(graph, first, Add<RootGraphNode>(graph), RootGraphNode.INPUT_PORT);
            Connect(graph, second, first, "Children_0");

            Assert.IsTrue(graph.Connect(first.GetOutputPort(0), second.GetInputPortByName("Children_0")));

            AssertError(CommandTreeGraphConverter.Convert(graph), "contains itself");
        }

        private static CommandTreeGraph CreateGraph(string name)
        {
            return GraphDatabase.CreateGraph<CommandTreeGraph>($"{FOLDER}/{name}.{CommandTreeGraph.EXTENSION}");
        }

        private static T Add<T>(Graph graph) where T : Node, new()
        {
            var node = new T();
            graph.AddNode(node);
            return node;
        }

        private static void SetChildCount(CommandTreeGraphNode node, int count)
        {
            Assert.IsTrue(node.GetNodeOptionByName("Children").TrySetValue(count));
            node.DefineNode();
            Assert.IsNotNull(node.GetInputPortByName($"Children_{count - 1}"));
        }

        private static void Connect(Graph graph, INode child, INode parent, string port)
        {
            var input = parent.GetInputPortByName(port);
            Assert.IsNotNull(input, port);
            Assert.IsTrue(graph.Connect(child.GetOutputPortByName(CommandTreeGraphNode.OUTPUT_PORT), input), port);
        }

        private static void ConnectVariable<T>(Graph graph, string name, INode node, string port)
        {
            var variable = graph.GetVariables().FirstOrDefault(v => v.Name == name)
                           ?? graph.CreateVariable(name, typeof(T), null, VariableKind.Local);
            var variableNode = graph.AddVariableNode(variable, Vector2.zero);
            Assert.IsTrue(graph.Connect(variableNode.GetOutputPort(0), node.GetInputPortByName(port)), port);
        }

        private static void AssertError(CommandTreeGraphConverter converter, string message)
        {
            Assert.IsTrue(converter.HasErrors);
            Assert.IsTrue(converter.Issues.Any(i => i.IsError && i.Message.Contains(message)),
                string.Join("\n", converter.Issues.Select(i => i.Message)));
        }
    }
}
