# Troubleshooting

A faulted run logs its exception once with `Debug.LogException` (or rethrows it from `await`); the run's handle
reports `CommandResult.Faulted`. Exceptions thrown straight from an API call are misuse.

## Exceptions of runs (logged, run Faulted)
| Message | Cause | Fix |
|---------|-------|-----|
| `Command processor for type 'X' not found` | `X` has no registration on this executor. | Add it to a registration context or `AddRegistration<X, XProcessor>()`. |
| `Command processor 'P' cannot be created` | The factory returned false and `P` has no public parameterless constructor, or its constructor threw. | Add a parameterless constructor, or create `P` in the `IObjectFactory`. |
| `P cannot process Y, it expects X` | The registration maps `Y` to a processor of another command type. | Fix the registration pair. |
| `Command group contains a null command` | A `CommandGroup.Commands` entry is null (often an empty `[SerializeReference]` slot). | Remove the slot or assign a command. |
| `X uses a preset, but none is assigned` | *Is Use Preset* is on (or `UsePreset` was called) and the preset is missing or destroyed. | Assign the preset or untick *Is Use Preset*. |
| `CommandExecutor.Tick is not re-entrant` | A command of a manual executor called its `Tick`. | Tick from outside the run. |
| Any exception of the game's processor | Thrown in `Start`, `Tick` or `Execute`. | The run's other running processors got `Cleanup(interrupted: true)`; fix the processor. Exceptions from `Cleanup` are logged without changing the result. |

## Exceptions of API calls (thrown)
| Exception | Cause |
|-----------|-------|
| `ArgumentNullException` | `Execute(null)`, `OnFinished(null)`, `UsePreset(null)`, `Build(null)`. |
| `ObjectDisposedException` | `Execute` on a disposed executor. A run started from a command while the executor was being disposed is cancelled. |
| `ArgumentException: ... must implement either IStatelessCommandProcessor or IStatefulCommandProcessor` | `AddRegistration` with a processor of neither or both contracts. |
| `InvalidOperationException: CommandExecutor is ticked by the player loop ...` | `Tick` on an auto-ticked executor: create it with `IsAutoTicked = false`. |
| `ArgumentOutOfRangeException: Unknown player loop phase` | A `CommandUpdatePhase` value outside the enum. |
| `InvalidOperationException: Tree preset 'N' has no root node` | `Build` on an empty preset, or on a graph that imported with errors. |
| `KeyNotFoundException: Tree preset arguments have no 'k' (T)` | `Build` args lack a key of the tree: compare with `GetRequiredKeys()`. |
| `InvalidCastException: Tree preset argument 'k' is A, expected B` | The value under `k` has the wrong type (`GameObject` for a `Transform` key, a lambda not cast to `Action`). |
| `InvalidOperationException: Tree preset key of T has no name` | A node's key field is empty (a `.asset` preset edited by hand). |
| `InvalidOperationException: XTreeNode has no node at index i` | A group node has a null child. |

## Nothing happens
- An auto-ticked executor only ticks in play mode; in EditMode tests use `IsAutoTicked = false` and `Tick`.
- `TimeMode.Scaled` stops while `Time.timeScale` is 0: use an `Unscaled` executor for pause menus.
- The executor was disposed (its scope ended), or the handle was cancelled by a restart (`_handle.Cancel()` before a
  new `Execute`).
- A command was released to the pool while still referenced: commands rented by the builder belong to the run.

## Graph messages (graph window and import log)
| Message | Fix |
|---------|-----|
| `The graph has no Output node: add one and wire the root command to it` | Add *Output*, wire the root. |
| `Wire a command node to the Output node` | Connect a command node's Out to Output. |
| `The graph has more than one Output node` | Delete the extra one. |
| `N: wire a T variable to Port` | Create a blackboard variable of type `T`, drag it in, wire it to the target port. |
| `N: Port takes a variable, whose name is the key, not a value` | Target ports take variables, not constants. |
| `N: Port takes a value or a constant; variables are keys of targets` | Value ports take inline values or constants. |
| `N: Port takes a T, variable 'v' is a U` | Change the variable's type. |
| `N: the variable wired to Port has no name` | Name the variable. |
| `N: wire a command node to i` | Child port `i` of a group is empty: fill it or lower the *Children* count. |
| `N contains itself` | Remove the cycle. |
| `Only command nodes can be wired to N` | Link ports take command nodes' Out only. |
| `N is not wired to the Output node and is not imported` (warning) | Wire it in or delete it. |
| `Command tree graphs do not support subgraphs` | Inline the subgraph's nodes. |
| `X.field: a field of type T cannot be edited in the graph` | The tree node has a field the graph cannot show; change its type or keep the node out of graphs. |

*Open in Graph Toolkit* refusals (dialog): `X has no graph node: declare a TreeNodeGraphNode<X> ...` (add a graph node),
`Key 'k' is a A in one node and a B in another ...` (rename one key), `... has n nodes, more than the 32 a graph node
takes` (split the group), `the graph would change the tree: ...` (a value the graph cannot represent).
Saving an editing graph logs `... was not saved: the graph ... has errors` (fix the graph and save again) or
`... edits a tree preset that no longer exists: nothing was saved`.
