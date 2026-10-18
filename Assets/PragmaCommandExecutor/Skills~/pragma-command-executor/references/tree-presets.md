# Tree presets and graphs

## The preset
`CommandTreePreset` (ScriptableObject) stores a tree of `ICommandTreePresetNode`s whose targets are string keys
(`CommandTreePresetKey<T>`).

```csharp
ICommand tree = preset.Build(args);                         // args: IReadOnlyDictionary<string, object>
IReadOnlyList<CommandTreePresetKeyInfo> keys = preset.GetRequiredKeys();   // (Name, Type) in tree order, no duplicates
```
- Every `Build` creates new commands with a copy of the node data: the preset never changes and the built tree is the
  caller's — `ExecuteAndRelease(tree)` to pool it after one run, or `Execute(tree)` many times while the targets stay.
- A missing key throws `KeyNotFoundException`, a value of another type `InvalidCastException` (both name the key); a
  `null` value is passed on (a `null` target makes a lerp complete at once, a `null` callback does nothing).
- Reuse one `Dictionary<string, object>` per caller to avoid allocating arguments every time.

Built-in nodes: `SequenceTreeNode`, `ParallelTreeNode` (`Repeat`, `Children`), `DelayTreeNode` (`Duration`),
`LogTreeNode` (`Message`, `LogType`), `CallbackTreeNode` (`Callback` key of `Action`), `ScaleTreeNode` (`Target` key of
`Transform`, `Data` = `LerpData<Vector3>`).

## Graphs (Unity 6000.6+)
*Assets → Create → Pragma → Command Executor → Tree Preset Graph* creates a `.commandtree` file that imports as a
`CommandTreePreset` (main object); reference it like any preset. Open it to edit in Graph Toolkit.

- **Output** node: wire the root command into it. Exactly one per graph.
- **Groups / Sequence, Parallel**: the *Children* option sets the number of child ports (0–32, default 2); *Repeat* is
  a value port. Wire command nodes' **Out** into the child ports in order.
- **Common / Delay, Log, Callback**, **Transform / Scale**: one port per serialized field.
- **Target ports** (keys) take a graph **variable**: blackboard variable of the target's type (`Transform`, `Action`);
  its name is the key passed to `Build`. **Value ports** take an inline value or a **constant** node, never a variable.
  Link ports take only the Out of command nodes.
- Errors (missing wires, unwired target, variable of the wrong type, missing or second Output, cycles, subgraphs) show
  in the graph while editing and are logged as import errors; such a graph imports as a preset **without a root**
  (`Build` then throws). Nodes not reachable from Output are a warning and are not imported.

### Editing a `.asset` preset
*Assets → Create → Pragma → Command Executor → Tree Preset* makes a `.asset` preset (its `[SerializeReference]` root has
no inspector picker). *Open in Graph Toolkit* — the Inspector header button, the preset's context menu or *Assets →
Open in Graph Toolkit* — builds `<Name> (editing).commandtree` next to it. Saving that graph writes the tree into the
`.asset` (references to the preset keep working; node objects that keep their type are reused, so the asset diff shows
only the change); a graph with errors writes nothing and logs a warning. The editing graph is deleted about two
seconds after no window shows it. Refused trees: a node type without a graph node, a key used with two types, more
than 32 children in one list, or anything the graph would change.

## Custom nodes
Any `[Serializable]` class implementing `ICommandTreePresetNode.Build(args)` is a node. For a
`Command<TContext, TData>` it is one line:

```csharp
[Serializable]
public class ShakeTreeNode : CommandTreeNode<ShakeCommand, Transform, ShakeData>   // Target key + Data copied per build
{
}
```
Hand-written nodes resolve their keys with `key.Resolve(args)` and must never return or share their own mutable data
(copy it). Generic node classes must be abstract.

To list a node in graphs, declare its graph node in an Editor assembly that references
`Pragma.CommandExecutor.GraphToolkit.Editor` (and is constrained to `UNITY_6000_6_OR_NEWER` if the game supports
older Unity versions):

```csharp
[Serializable, Node("Game", null, "Shake"), UseWithGraph(typeof(CommandTreeGraph))]
public class ShakeGraphNode : TreeNodeGraphNode<ShakeTreeNode>
{
}
```
Ports come from the fields Unity serializes on the tree node, base classes first:
- `CommandTreePresetKey<T>` → target port of type `T`; a list of keys → a multi target port.
- `ICommandTreePresetNode` → link port; a list of nodes → the *Children*-style count option plus one link port each.
- Nested `[Serializable]` classes → flattened ports named `Field_Nested` (`Data_From`, shown as *From* unless two ports
  would read the same).
- Primitives, enums, strings, `UnityEngine.Object` references, vectors, colors, `AnimationCurve`, `Gradient`… → value
  ports. Other types (dictionaries, interfaces other than nodes) make the node unusable in graphs.
Port names are the field names: renaming a field disconnects its wires; renaming a node type breaks the graphs.

## Creating a graph from code (Editor)
Graph files can be generated, e.g. in an editor script run through `unity command run_script`:

```csharp
var graph = GraphDatabase.CreateGraph<CommandTreeGraph>("Assets/Presets/Pulse.commandtree");
var output = new RootGraphNode();
graph.AddNode(output);
var sequence = new SequenceGraphNode();
graph.AddNode(sequence);
sequence.GetNodeOptionByName("Children").TrySetValue(2);
sequence.DefineNode();                                            // creates Children_0, Children_1
var scale = new ScaleGraphNode();
graph.AddNode(scale);
var delay = new DelayGraphNode();
graph.AddNode(delay);

graph.Connect(sequence.GetOutputPortByName(CommandTreeGraphNode.OUTPUT_PORT), output.GetInputPortByName(RootGraphNode.INPUT_PORT));
graph.Connect(scale.GetOutputPortByName(CommandTreeGraphNode.OUTPUT_PORT), sequence.GetInputPortByName("Children_0"));
graph.Connect(delay.GetOutputPortByName(CommandTreeGraphNode.OUTPUT_PORT), sequence.GetInputPortByName("Children_1"));

var target = graph.AddVariableNode(graph.CreateVariable("target", typeof(Transform), null, VariableKind.Local), Vector2.zero);
graph.Connect(target.GetOutputPort(0), scale.GetInputPortByName("Target"));
scale.GetInputPortByName("Data_To").TrySetValue(new Vector3(1.5f, 1.5f, 1.5f));
delay.GetInputPortByName("Duration").TrySetValue(0.2f);

GraphDatabase.SaveGraph(graph);
AssetDatabase.ImportAsset("Assets/Presets/Pulse.commandtree", ImportAssetOptions.ForceSynchronousImport);
```
Node positions default to the origin; set `node.Position` for a readable layout. After the import, load the preset
again (`AssetDatabase.LoadAssetAtPath<CommandTreePreset>(path)`) before assigning it to scene objects: a reimport
replaces the preset object, and a reference to the old one serializes as missing.
