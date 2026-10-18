---
name: pragma-command-executor
description: "Pragma Command Executor (com.pragma.commandexecutor): tick based trees of commands — sequences, parallel groups, delays, lerps, callbacks — with custom commands and processors, shared data presets and tree presets edited as Graph Toolkit graphs. Use to add, debug or change command trees, custom commands or tree presets in a Unity project that uses the package."
---
# pragma-command-executor

Verified: com.pragma.commandexecutor at the commit that ships this skill, Unity 6000.6.4f1 (tree preset graphs need
6000.6+; the rest runs on 2021.2+). Everything is in namespace `Pragma.CommandExecutor`, assembly
`Pragma.CommandExecutor` (UniTask integration: `Pragma.CommandExecutor.UniTask`, graphs:
`Pragma.CommandExecutor.GraphToolkit.Editor`).

## Model
- A **command** is data (`ICommand`): what to do and, for a `Command<TContext, TData>`, its target (`Context`) and
  settings (`Data`, inline or from a shared `CommandPresetConfig<TData>` asset). `Reset()` clears it for the pool.
- A **processor** runs one command type. Exactly one of two contracts:
  `IStatelessCommandProcessor<T>.Execute(command)` — instant, one shared instance, no runtime node;
  `IStatefulCommandProcessor<T>` — pooled per run: `Start(command)`, then `Tick(0)` in the same frame, then
  `Tick(deltaTime)` once per frame while it returns `CommandStatus.Running`, then `Cleanup(interrupted)` exactly once.
- A **tree** is a `CommandGroup` (`GroupMode.Sequential` / `Parallel`, `Repeat` extra passes, `REPEAT_FOREVER`) of
  commands: built with `CommandBuilder`, serialized in an inspector field (`[SerializeReference]` children), or built
  from a `CommandTreePreset` whose targets are string keys.
- The **executor** (`CommandExecutor : ICommandExecutor, IDisposable`) maps command types to processor types
  (`ICommandRegistrationContext`, `AddRegistration`), creates processors through an `IObjectFactory`, starts a run
  synchronously inside `Execute` and ticks every run once per frame at the player loop phase of its
  `CommandExecutorSettings`, on scaled or unscaled time — or only through `Tick(deltaTime)` when `IsAutoTicked` is false.
- A **run** is a versioned `CommandHandle`: `IsRunning`, `Cancel()`, `OnFinished(CommandResult)` (`Completed`,
  `Cancelled`, `Faulted`). Runs never throw: a processor exception faults the run and is logged once. API misuse
  (null arguments, a disposed executor, unknown phase) throws.
- Commands are pure data: never keep run state in a command; the same tree may run several times at once.

## Wire into a game
1. Package: `"com.pragma.commandexecutor": "https://github.com/PragmaGame/PragmaCommandExecutor.git?path=Assets/PragmaCommandExecutor"`
   (append `#<tag or commit>` to pin). Game asmdefs reference `Pragma.CommandExecutor`; `Assembly-CSharp` needs nothing.
2. Create one executor per scope (game, level, UI screen), dispose it with the scope — disposing cancels its runs:
```csharp
var executor = new CommandExecutor(
    factory: null,                                   // null = ActivatorFactory (parameterless constructors)
    registrationContexts: new ICommandRegistrationContext[]
    {
        new DefaultCommandRegistrationContext(),     // Delay, Callback, Log, Scale
        new GameCommandRegistrationContext(),        // the game's own; a later context overrides an earlier one
    },
    settings: new CommandExecutorSettings { UpdatePhase = CommandUpdatePhase.Update, TimeMode = CommandTimeMode.Scaled });
```
   VContainer ignores default parameter values: register a `CommandExecutorSettings` instance, the contexts and an
   `IObjectFactory` (references/custom-commands.md → *Object factory and dependency injection*). Zenject: bind the
   factory and the contexts the same way, `BindInstance(new CommandExecutorSettings())` and
   `BindInterfacesAndSelfTo<CommandExecutor>().AsSingle()`.
3. Optional defines (*Player → Scripting Define Symbols*, every platform): `COMMAND_EXECUTOR_UNITASK_SUPPORT` compiles the
   UniTask integration (UniTask must be in the project); `COMMAND_EXECUTOR_SINGLETON_ENABLED` adds `CommandExecutor.Singleton`.

## API by moment
| Moment | Call |
|--------|------|
| Build and run a tree | `executor.GetBuilder(GroupMode.Sequential).JoinScale(t, from, to, 0.3f).JoinDelay(0.2f).JoinGroup(GroupMode.Parallel, g => g.JoinCallback(a).JoinLog("x", LogType.Log), repeat: 1).Execute()` |
| Configure a pooled command without a closure | `builder.Join(out ScaleCommand scale); scale.Context = t; scale.Data.To = v;` |
| Run a command you own (also serialized trees) | `executor.Execute(command)` — never released, may run concurrently |
| Run and return to the pools | `executor.ExecuteAndRelease(tree, excluded)`, `executor.Execute<T>(c => ...)` |
| Prebuild once, run many times | `var tree = builder.Build();` … `executor.Execute(tree)` … `executor.ReleaseCommand(tree)` |
| Cancel / observe | `handle.Cancel()`, `handle.OnFinished(r => ...)`, `handle.IsRunning` |
| Await (UniTask define) | `await handle;` `await executor.Execute(cmd, token);` `await builder.Execute(token);` |
| Shared settings | `command.UsePreset(preset)` / *Is Use Preset* in the inspector; `JoinScale(t, preset)` |
| Tree preset | `executor.ExecuteAndRelease(preset.Build(args))`, keys from `preset.GetRequiredKeys()` |
| Manual clock | `new CommandExecutorSettings { IsAutoTicked = false }`, then `executor.Tick(deltaTime)` |

Details, timing rules and ownership: references/usage.md.

## Recipes
- **Custom command:** a `[Serializable]` data class with `Reset()`, a processor implementing exactly one contract,
  a registration, optionally a builder shortcut over `Join(out command)` — references/custom-commands.md.
- **Target + shared settings:** derive the command from `Command<TContext, TData>` (lerps: `LerpCommand<TContext, TValue>`
  + `LerpCommandProcessor<TCommand, TContext, TValue>`), add a non-generic `CommandPresetConfig<TData>` subclass with
  `[CreateAssetMenu]` for presets — references/custom-commands.md.
- **Designer-authored tree:** a `.commandtree` graph (*Assets → Create → Pragma → Command Executor → Tree Preset Graph*,
  Unity 6000.6+) or a `.asset` preset opened with *Open in Graph Toolkit*; code builds it with the targets as arguments —
  references/tree-presets.md.

```csharp
public class Door : MonoBehaviour
{
    [SerializeField] private CommandTreePreset _open;      // a .commandtree graph with variables "door" and "opened"
    private readonly Dictionary<string, object> _args = new();
    private ICommandExecutor _executor;
    private CommandHandle _handle;

    public void Construct(ICommandExecutor executor) => _executor = executor;

    public void Open()
    {
        _handle.Cancel();                                   // a default or finished handle ignores it
        _args["door"] = transform;
        _args["opened"] = (Action)OnOpened;
        _handle = _executor.ExecuteAndRelease(_open.Build(_args));
    }

    private void OnOpened() => Debug.Log("Door opened");

    private void OnDisable() => _handle.Cancel();           // runs are not tied to GameObjects
}
```

## Gotchas
- `Execute` is synchronous: instant commands at the head of the tree (callbacks, logs, zero durations, stateless
  commands) run inside the call, and a fully instant tree returns an already finished handle — `OnFinished` then
  fires immediately with `Completed`. A fault inside that synchronous start is only logged, never awaited.
- Runs are not bound to GameObjects: cancel them (or dispose the executor) when the target is destroyed. A lerp whose
  Unity target was destroyed completes on its next step.
- Builder-rented commands go back to the pool when the run ends; never keep a reference to them after `Execute`.
  Commands added with `Join(ICommand)`, serialized trees and `Build()` results stay yours.
- A cancelled run completes `await` **normally**; check the token afterwards. An already cancelled token runs nothing.
- `Cancel()` from inside the run (a callback) takes effect when the current step returns.
- A processor registered for a command must implement exactly one of `IStatelessCommandProcessor<T>` and
  `IStatefulCommandProcessor<T>`, or `AddRegistration` throws. Instant work belongs in a stateless processor.
- `CommandExecutorSettings` are copied by the constructor; `Tick` throws on an auto-ticked executor.
- A preset is shared, not copied: editing it changes every command using it. A command set to use a missing preset
  faults its run.
- Renaming or moving a command, tree node or graph node type (class, namespace, assembly) breaks serialized scenes,
  presets and graphs that reference it. Renaming a field of a tree node disconnects its wires in graphs.
- Zero allocations once pools are warm only for builder trees, prebuilt trees and ticking: lambdas that capture
  variables allocate a closure each time; a `Command<TContext, TData>` with class data must reset it in place in
  `OnResetData` to stay allocation free.

## Checks
- EditMode test with a manual clock: `new CommandExecutor(null, contexts, new CommandExecutorSettings { IsAutoTicked = false })`,
  `executor.Tick(0.25f)` per frame, assert on targets and `handle.IsRunning` — references/usage.md → *Testing*.
- Graph problems show in the graph window and as import errors on the `.commandtree` asset; messages and fixes:
  references/troubleshooting.md.
- Exceptions of faulted runs are logged with `Debug.LogException` from the executor; read the console.

## References
- references/usage.md — building, executing, groups, timing, handles and errors, UniTask, settings, testing.
- references/custom-commands.md — processor contracts, lifecycle, command data and presets, lerps, builder shortcuts,
  registration, object factory and DI, code stripping.
- references/tree-presets.md — `CommandTreePreset`, keys, graphs, *Open in Graph Toolkit*, custom nodes, creating
  graphs from code.
- references/troubleshooting.md — every exception and graph message, what it means and what to do.
