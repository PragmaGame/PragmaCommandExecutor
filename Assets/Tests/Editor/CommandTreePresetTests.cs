using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pragma.CommandExecutor.Tests
{
    public class CommandTreePresetTests
    {
        // Exactly representable in binary, so accumulated time never drifts.
        private const float FRAME = 0.25f;
        private const float TOLERANCE = 1e-4f;

        private CommandExecutor _executor;
        private List<Object> _objects;

        [SetUp]
        public void SetUp()
        {
            _executor = new CommandExecutor(
                null,
                new ICommandRegistrationContext[] { new DefaultCommandRegistrationContext() },
                new CommandExecutorSettings { IsAutoTicked = false });

            _objects = new List<Object>();
        }

        [TearDown]
        public void TearDown()
        {
            _executor.Dispose();

            foreach (var unityObject in _objects)
            {
                if (unityObject != null)
                {
                    Object.DestroyImmediate(unityObject);
                }
            }
        }

        [Test]
        public void Build_Sequence_BuildsGroupWithChildrenInOrder()
        {
            var preset = Preset(new SequenceTreeNode
            {
                Repeat = 2,
                Children = { new DelayTreeNode { Duration = 0.5f }, Scale("hero", Vector3.right), new LogTreeNode() },
            });

            var group = (CommandGroup)preset.Build(Args(("hero", CreateTransform())));

            Assert.AreEqual(GroupMode.Sequential, group.Mode);
            Assert.AreEqual(2, group.Repeat);
            CollectionAssert.AreEqual(
                new[] { typeof(DelayCommand), typeof(ScaleCommand), typeof(LogCommand) },
                group.Commands.Select(c => c.GetType()));
            Assert.AreEqual(0.5f, ((DelayCommand)group.Commands[0]).Duration);
        }

        [Test]
        public void Build_Parallel_BuildsParallelGroup()
        {
            var preset = Preset(new ParallelTreeNode { Children = { new DelayTreeNode(), new DelayTreeNode() } });

            var group = (CommandGroup)preset.Build(Args());

            Assert.AreEqual(GroupMode.Parallel, group.Mode);
            Assert.AreEqual(2, group.Commands.Count);
        }

        [Test]
        public void Execute_Scale_ScalesTheTransformFromArgs()
        {
            var hero = CreateTransform();
            var preset = Preset(Scale("hero", new Vector3(2f, 2f, 2f)));

            var handle = _executor.Execute(preset.Build(Args(("hero", hero))));

            Tick();
            Assert.AreEqual(1f, hero.localScale.x, TOLERANCE);
            Tick();
            Assert.AreEqual(2f, hero.localScale.x, TOLERANCE);
            Assert.IsFalse(handle.IsRunning);
        }

        [Test]
        public void Execute_Sequence_ScalesThenCallsBack()
        {
            var log = new List<string>();
            var hero = CreateTransform();
            var preset = Preset(new SequenceTreeNode
            {
                Children = { Scale("hero", Vector3.one * 3f), new CallbackTreeNode { Callback = "done" } },
            });

            var args = Args(("hero", hero), ("done", (Action)(() => log.Add($"done at {hero.localScale.x}"))));
            _executor.Execute(preset.Build(args));
            Tick(2);

            CollectionAssert.AreEqual(new[] { "done at 3" }, log);
        }

        [Test]
        public void Build_MissingKey_ThrowsKeyNotFoundNamingTheKey()
        {
            var preset = Preset(Scale("hero", Vector3.one));

            var exception = Assert.Throws<KeyNotFoundException>(() => preset.Build(Args(("enemy", CreateTransform()))));
            StringAssert.Contains("'hero'", exception.Message);
            StringAssert.Contains(nameof(Transform), exception.Message);
        }

        [Test]
        public void Build_ValueOfAnotherType_ThrowsInvalidCast()
        {
            var preset = Preset(Scale("hero", Vector3.one));

            var exception = Assert.Throws<InvalidCastException>(() => preset.Build(Args(("hero", CreateTransform().gameObject))));
            StringAssert.Contains("'hero'", exception.Message);
        }

        [Test]
        public void Build_NullValue_IsPassedToTheCommand()
        {
            var preset = Preset(new CallbackTreeNode { Callback = "done" });

            var command = (CallbackCommand)preset.Build(Args(("done", null)));

            Assert.IsNull(command.Callback);
        }

        [Test]
        public void Build_WithoutRootOrArgs_Throws()
        {
            var empty = Preset(null);

            Assert.Throws<InvalidOperationException>(() => empty.Build(Args()));
            Assert.Throws<ArgumentNullException>(() => Preset(new DelayTreeNode()).Build(null));
        }

        [Test]
        public void Build_GroupWithNullChild_ThrowsWithTheIndex()
        {
            var preset = Preset(new SequenceTreeNode { Children = { new DelayTreeNode(), null } });

            var exception = Assert.Throws<InvalidOperationException>(() => preset.Build(Args()));
            StringAssert.Contains("index 1", exception.Message);
        }

        [Test]
        public void Build_EveryCall_CreatesNewCommandsWithCopiedData()
        {
            var node = Scale("hero", Vector3.one);
            var preset = Preset(node);
            var args = Args(("hero", CreateTransform()));

            var first = (ScaleCommand)preset.Build(args);
            var second = (ScaleCommand)preset.Build(args);

            Assert.AreNotSame(first, second);
            Assert.AreNotSame(node.Data, first.Data);
            Assert.AreNotSame(first.Data, second.Data);
            Assert.AreNotSame(node.Data.Curve, first.Data.Curve);
            Assert.AreEqual(node.Data.Curve.keys.Length, first.Data.Curve.keys.Length);
            Assert.AreEqual(Vector3.one, first.Data.To);
        }

        [Test]
        public void ExecuteAndRelease_BuiltTree_LeavesThePresetIntact()
        {
            var scale = Scale("hero", Vector3.one);
            var preset = Preset(new SequenceTreeNode { Children = { scale, new DelayTreeNode { Duration = FRAME } } });

            _executor.ExecuteAndRelease(preset.Build(Args(("hero", CreateTransform()))));
            Tick(4);

            Assert.AreEqual(Vector3.one, scale.Data.To);
            Assert.AreEqual(FRAME * 2, scale.Data.Duration);
            Assert.IsNotNull(scale.Data.Curve);
            Assert.AreEqual(2, ((SequenceTreeNode)preset.Root).Children.Count);
            Assert.AreEqual(FRAME, ((DelayTreeNode)((SequenceTreeNode)preset.Root).Children[1]).Duration);
        }

        [Test]
        public void Instantiate_TreeSurvivesUnitySerialization()
        {
            var preset = Preset(new SequenceTreeNode
            {
                Repeat = 3,
                Children =
                {
                    Scale("hero", new Vector3(1f, 2f, 3f)),
                    new LogTreeNode { Message = "hello", LogType = LogType.Warning },
                    new CallbackTreeNode { Callback = "done" },
                },
            });

            var copy = Object.Instantiate(preset);
            _objects.Add(copy);

            var sequence = (SequenceTreeNode)copy.Root;
            Assert.AreNotSame(preset.Root, sequence);
            Assert.AreEqual(3, sequence.Repeat);
            var scale = (ScaleTreeNode)sequence.Children[0];
            Assert.AreEqual("hero", scale.Target.Name);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), scale.Data.To);
            var log = (LogTreeNode)sequence.Children[1];
            Assert.AreEqual("hello", log.Message);
            Assert.AreEqual(LogType.Warning, log.LogType);
            Assert.AreEqual("done", ((CallbackTreeNode)sequence.Children[2]).Callback.Name);
        }

        [Test]
        public void GetRequiredKeys_ListsEachKeyOnceWithItsType()
        {
            var preset = Preset(new SequenceTreeNode
            {
                Children =
                {
                    Scale("hero", Vector3.one),
                    new ParallelTreeNode { Children = { Scale("hero", Vector3.zero), Scale("enemy", Vector3.one) } },
                    new CallbackTreeNode { Callback = "done" },
                },
            });

            CollectionAssert.AreEqual(
                new[]
                {
                    new CommandTreePresetKeyInfo("hero", typeof(Transform)),
                    new CommandTreePresetKeyInfo("enemy", typeof(Transform)),
                    new CommandTreePresetKeyInfo("done", typeof(Action)),
                },
                preset.GetRequiredKeys());
        }

        [Test]
        public void EveryRegisteredCommand_HasATreeNodeThatBuildsIt()
        {
            var built = new HashSet<Type>();

            foreach (var nodeType in PackageNodeTypes().Where(t => !t.IsAbstract))
            {
                var node = (ICommandTreePresetNode)Activator.CreateInstance(nodeType);
                NameEveryKey(node, "target");
                built.Add(node.Build(Args(("target", null))).GetType());
            }

            var registered = new DefaultCommandRegistrationContext().Registrations.Keys;
            CollectionAssert.IsSubsetOf(registered, built);
        }

        [Test]
        public void GenericTreeNodes_AreAbstract()
        {
            // An open generic node cannot be created by the graph or the serializer.
            foreach (var nodeType in PackageNodeTypes().Where(t => t.ContainsGenericParameters))
            {
                Assert.IsTrue(nodeType.IsAbstract, nodeType.Name);
            }
        }

        private static IEnumerable<Type> PackageNodeTypes()
        {
            return typeof(ICommandTreePresetNode).Assembly.GetTypes()
                .Where(t => typeof(ICommandTreePresetNode).IsAssignableFrom(t) && !t.IsInterface);
        }

        private static void NameEveryKey(object node, string name)
        {
            for (var type = node.GetType(); type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var fieldType = field.FieldType;

                    if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(CommandTreePresetKey<>))
                    {
                        field.SetValue(node, Activator.CreateInstance(fieldType, name));
                    }
                }
            }
        }

        private CommandTreePreset Preset(ICommandTreePresetNode root)
        {
            var preset = ScriptableObject.CreateInstance<CommandTreePreset>();
            preset.Root = root;
            _objects.Add(preset);
            return preset;
        }

        private static ScaleTreeNode Scale(string key, Vector3 to)
        {
            return new ScaleTreeNode
            {
                Target = key,
                Data = new LerpData<Vector3>
                {
                    From = Vector3.zero,
                    To = to,
                    Duration = FRAME * 2,
                    Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                },
            };
        }

        private Transform CreateTransform()
        {
            var gameObject = new GameObject(nameof(CommandTreePresetTests));
            _objects.Add(gameObject);
            return gameObject.transform;
        }

        private static Dictionary<string, object> Args(params (string key, object value)[] values)
        {
            var args = new Dictionary<string, object>();

            foreach (var (key, value) in values)
            {
                args[key] = value;
            }

            return args;
        }

        private void Tick(int frames = 1)
        {
            for (var i = 0; i < frames; i++)
            {
                _executor.Tick(FRAME);
            }
        }
    }
}
