using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Pragma.CommandExecutor.Tests
{
    public class CommandTreeGraphEditingTests
    {
        private const string FOLDER = "Assets/CommandTreeGraphEditingTests";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(FOLDER))
            {
                AssetDatabase.CreateFolder("Assets", "CommandTreeGraphEditingTests");
            }
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(FOLDER);
        }

        [Test]
        public void Build_EveryPackageNode_ConvertsBackToTheSameTree()
        {
            var parallel = new ParallelTreeNode { Repeat = 2 };
            var index = 0;

            foreach (var type in typeof(ICommandTreePresetNode).Assembly.GetTypes()
                         .Where(t => typeof(ICommandTreePresetNode).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                         .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                var node = (ICommandTreePresetNode)Activator.CreateInstance(type);
                Fill(node, index++);
                parallel.Children.Add(node);
            }

            // As Unity deserializes it: empty curves and strings rather than nulls.
            var preset = Object.Instantiate(CreatePreset("RoundTrip", parallel));
            var graph = GraphDatabase.CreateGraph<CommandTreeGraph>($"{FOLDER}/RoundTrip.{CommandTreeGraph.EXTENSION}");

            CollectionAssert.IsEmpty(CommandTreeGraphBuilder.Validate(preset.Root));
            CommandTreeGraphBuilder.Build(graph, preset.Root);
            var converter = CommandTreeGraphConverter.Convert(graph);

            CollectionAssert.IsEmpty(converter.Issues, string.Join("\n", converter.Issues));
            Assert.AreEqual(CommandTreeGraphEditing.SerializeTree(preset.Root), CommandTreeGraphEditing.SerializeTree(converter.Root));
            Object.DestroyImmediate(preset);
        }

        [Test]
        public void CreateEditingGraph_IsNextToThePreset_AndImportsAsAnEditingGraph()
        {
            var preset = CreatePreset("Hop", CreateHop());

            var path = CommandTreeGraphEditing.CreateEditingGraph(preset, out var error);

            Assert.IsNull(error);
            Assert.AreEqual($"{FOLDER}/Hop (editing).{CommandTreeGraph.EXTENSION}", path);
            var editingGraph = AssetDatabase.LoadMainAssetAtPath(path) as CommandTreeEditingGraph;
            Assert.IsNotNull(editingGraph);
            Assert.AreSame(preset, editingGraph.Preset);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<CommandTreePreset>(path));
            Assert.AreEqual(path, CommandTreeGraphEditing.FindEditingGraph(Guid(preset)));
        }

        [Test]
        public void SavingTheEditingGraph_WritesThePreset_AndKeepsItsNodeObjects()
        {
            var preset = CreatePreset("Hop", CreateHop());
            var rootId = ManagedReferenceUtility.GetManagedReferenceIdForObject(preset, preset.Root);
            var path = CommandTreeGraphEditing.CreateEditingGraph(preset, out _);
            var graph = GraphDatabase.LoadGraph<CommandTreeGraph>(path);

            Assert.IsTrue(graph.GetNodes().OfType<DelayGraphNode>().Single().GetInputPortByName("Duration").TrySetValue(2f));
            Save(graph);

            var root = (SequenceTreeNode)preset.Root;
            Assert.AreEqual(2f, ((DelayTreeNode)root.Children[1]).Duration);
            Assert.AreEqual("icon", ((ScaleTreeNode)root.Children[0]).Target.Name);
            Assert.AreEqual(rootId, ManagedReferenceUtility.GetManagedReferenceIdForObject(preset, preset.Root));
            Assert.IsFalse(EditorUtility.IsDirty(preset));
        }

        [Test]
        public void SavingAGraphWithErrors_KeepsThePreset()
        {
            var preset = CreatePreset("Hop", CreateHop());
            var path = CommandTreeGraphEditing.CreateEditingGraph(preset, out _);
            var graph = GraphDatabase.LoadGraph<CommandTreeGraph>(path);
            var target = graph.GetNodes().OfType<ScaleGraphNode>().Single().GetInputPortByName("Target");

            graph.Disconnect(target.FirstConnectedPort, target);
            LogAssert.Expect(LogType.Error, new Regex("variable to Target"));
            Save(graph);

            Assert.AreEqual("icon", ((ScaleTreeNode)((SequenceTreeNode)preset.Root).Children[0]).Target.Name);
        }

        [Test]
        public void ReimportingAnUnchangedGraph_KeepsThePreset()
        {
            var preset = CreatePreset("Hop", CreateHop());
            var path = CommandTreeGraphEditing.CreateEditingGraph(preset, out _);
            var delay = (DelayTreeNode)((SequenceTreeNode)preset.Root).Children[1];

            delay.Duration = 9f;
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssetIfDirty(preset);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Assert.AreEqual(9f, delay.Duration);
        }

        [Test]
        public void CreateEditingGraph_KeyWithTwoTypes_IsRefused()
        {
            var parallel = new ParallelTreeNode();
            parallel.Children.Add(new ScaleTreeNode { Target = new CommandTreePresetKey<Transform>("icon") });
            parallel.Children.Add(new CallbackTreeNode { Callback = new CommandTreePresetKey<Action>("icon") });
            var preset = CreatePreset("TwoTypes", parallel);

            Assert.IsNull(CommandTreeGraphEditing.CreateEditingGraph(preset, out var error));
            StringAssert.Contains("Key 'icon'", error);
            Assert.IsNull(CommandTreeGraphEditing.FindEditingGraph(Guid(preset)));
        }

        [Test]
        public void CreateEditingGraph_NodeWithoutAGraphNode_IsRefused()
        {
            var preset = CreatePreset("Orphan", new OrphanTreeNode());

            Assert.IsNull(CommandTreeGraphEditing.CreateEditingGraph(preset, out var error));
            StringAssert.Contains("OrphanTreeNode has no graph node", error);
        }

        [Test]
        public void CheckWindows_DeletesTheEditingGraph_OnceNoWindowShowsIt()
        {
            var preset = CreatePreset("Hop", CreateHop());
            var path = CommandTreeGraphEditing.CreateEditingGraph(preset, out _);
            var graphPreset = $"{FOLDER}/Graph.{CommandTreeGraph.EXTENSION}";
            GraphDatabase.SaveGraph(GraphDatabase.CreateGraph<CommandTreeGraph>(graphPreset));

            CommandTreeGraphEditing.CheckWindows(new[] { path });
            CommandTreeGraphEditing.CheckWindows(Array.Empty<string>());
            Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(path));

            CommandTreeGraphEditing.CheckWindows(Array.Empty<string>());
            Assert.IsNull(AssetDatabase.LoadMainAssetAtPath(path));
            Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(graphPreset));
        }

        private static SequenceTreeNode CreateHop()
        {
            return new SequenceTreeNode
            {
                Children =
                {
                    new ScaleTreeNode { Target = new CommandTreePresetKey<Transform>("icon") },
                    new DelayTreeNode { Duration = 0.5f },
                },
            };
        }

        private static CommandTreePreset CreatePreset(string name, ICommandTreePresetNode root)
        {
            var preset = ScriptableObject.CreateInstance<CommandTreePreset>();
            preset.Root = root;
            AssetDatabase.CreateAsset(preset, $"{FOLDER}/{name}.asset");
            AssetDatabase.SaveAssets();
            return preset;
        }

        private static void Save(Graph graph)
        {
            // Saving imports the graph, as saving it in its window does.
            GraphDatabase.SaveGraph(graph);
        }

        private static string Guid(Object asset)
        {
            return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
        }

        /// <summary>Names every key after its type and gives every value port a value other than its default.</summary>
        private static void Fill(ICommandTreePresetNode node, int seed)
        {
            foreach (var port in TreeNodeSchema.Get(node.GetType()).Ports)
            {
                switch (port.Kind)
                {
                    case TreeNodePortKind.Key:
                        SetValue(node, port.Path, Activator.CreateInstance(port.FieldType, KeyName(port.DataType)));
                        break;
                    case TreeNodePortKind.KeyList:
                        TreeNodeSchema.TryGetListElementType(port.FieldType, out var keyType);
                        var keys = (IList)Activator.CreateInstance(port.FieldType);
                        keys.Add(Activator.CreateInstance(keyType, KeyName(port.DataType) + "B"));
                        keys.Add(Activator.CreateInstance(keyType, KeyName(port.DataType) + "A"));
                        SetValue(node, port.Path, keys);
                        break;
                    case TreeNodePortKind.Value:
                        var value = CreateValue(port.DataType, seed);

                        if (value != null)
                        {
                            SetValue(node, port.Path, value);
                        }

                        break;
                }
            }
        }

        /// <summary>A key name per target type: <c>Action&lt;float&gt;</c> and <c>Action&lt;int&gt;</c> differ.</summary>
        private static string KeyName(Type type)
        {
            return type.IsGenericType
                ? type.Name.Substring(0, type.Name.IndexOf('`')) + string.Concat(type.GetGenericArguments().Select(KeyName))
                : type.Name;
        }

        private static object CreateValue(Type type, int seed)
        {
            if (type == typeof(float))
            {
                return 0.25f + seed;
            }

            if (type == typeof(int))
            {
                return 3 + seed;
            }

            if (type == typeof(bool))
            {
                return true;
            }

            if (type == typeof(string))
            {
                return "value" + seed;
            }

            if (type == typeof(Vector2))
            {
                return new Vector2(1f, seed);
            }

            if (type == typeof(Vector3))
            {
                return new Vector3(1f, 2f, seed);
            }

            if (type == typeof(Color))
            {
                return new Color(0.5f, 0.25f, 1f, 0.75f);
            }

            if (type == typeof(AnimationCurve))
            {
                return AnimationCurve.EaseInOut(0f, 0f, 1f, seed);
            }

            if (type.IsEnum)
            {
                var values = Enum.GetValues(type);
                return values.GetValue(values.Length - 1);
            }

            return null;
        }

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
    }

    [Serializable]
    public class OrphanTreeNode : ICommandTreePresetNode
    {
        public ICommand Build(IReadOnlyDictionary<string, object> args) => new DelayCommand();
    }
}
