# Pragma Command Executor

A tick based command executor for Unity. Describe animations, gameplay sequences or UI flows as trees of small
commands — run them one after another, in parallel, repeated or forever — and let a single player loop tick drive
every tree: no coroutines, no `async` state machines, no GC allocations once the pools are warm.

```csharp
executor.GetBuilder(GroupMode.Sequential)
    .JoinScale(door, Vector3.one, new Vector3(1f, 0f, 1f), 0.3f)
    .JoinGroup(GroupMode.Parallel, parallel => parallel
        .JoinCallback(PlayOpenSound)
        .JoinDelay(0.5f))
    .JoinLog("Door opened", LogType.Log)
    .Execute();
```

## Contents

- [Features](#features)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Concepts](#concepts)
- [Building and running trees](#building-and-running-trees)
- [Timing](#timing)
- [Handles, results and errors](#handles-results-and-errors)
- [Custom commands](#custom-commands)
- [Registration](#registration)
- [Object factory and dependency injection](#object-factory-and-dependency-injection)
- [UniTask integration](#unitask-integration)
- [Singleton](#singleton)
- [Code stripping](#code-stripping)
- [Performance](#performance)
- [Examples](#examples)
- [Package layout](#package-layout)
- [Development](#development)

## Features

- **Sequential and parallel groups** nested to any depth; a group can repeat N times or forever.
- **Custom commands in a few lines:** a data class and a processor with `Start` / `Tick` / `Cleanup`.
- **One global tick** at the beginning of the player loop `Update` phase ticks every executor.
- **Pooled everything:** commands, processors, runtime nodes and runs. Ticking, executing prebuilt trees and building
  trees with the builder make no GC allocations once the pools are warm — enforced by the test suite.
- **Versioned handles** to cancel a run and to get its `CommandResult` (`Completed`, `Cancelled`, `Faulted`).
- **Runs never throw:** an exception in a processor faults the run, interrupts what is still running and is logged.
- **Serializable trees:** commands and groups are `[Serializable]` data with `[SerializeReference]` children.
- **Dependency injection friendly:** processors are created through an `IObjectFactory`.
- **Optional UniTask integration:** awaitable handles, token overloads and `async` processors.
- **Survives High managed code stripping,** including processors declared in your own assemblies.

## Installation

*Window → Package Manager → + → Add package from git URL…*

```
https://github.com/PragmaGame/PragmaCommandExecutor.git?path=Assets/PragmaCommandExecutor
```

or add it to `Packages/manifest.json`:

```json
"com.pragma.commandexecutor": "https://github.com/PragmaGame/PragmaCommandExecutor.git?path=Assets/PragmaCommandExecutor"
```

Append `#<tag or commit>` to the URL to pin a version.

Requirements: developed and tested with Unity 6000.3. The code needs C# 9 and default interface methods
(Unity 2021.2 or newer). [UniTask](https://github.com/Cysharp/UniTask) is optional.

## Quick start

```csharp
using Pragma.CommandExecutor;
using UnityEngine;

public class Door : MonoBehaviour
{
    private CommandExecutor _executor;
    private CommandHandle _handle;

    private void Awake()
    {
        // Registers itself in the player loop and is ticked every frame until disposed.
        _executor = new CommandExecutor(null, new ICommandRegistrationContext[]
        {
            new DefaultCommandRegistrationContext(),
        });
    }

    public void Open()
    {
        _handle.Cancel();

        _handle = _executor.GetBuilder(GroupMode.Sequential)
            .JoinScale(transform, Vector3.one, new Vector3(1f, 0f, 1f), 0.3f)
            .JoinDelay(0.2f)
            .JoinLog("Door opened", LogType.Log)
            .Execute();

        _handle.OnFinished(result => Debug.Log($"Open: {result}"));
    }

    private void OnDestroy()
    {
        // Cancels every run that is still going.
        _executor.Dispose();
    }
}
```

## Concepts

| Type | Role |
|------|------|
| `ICommand` | A command: plain data describing *what* to do (`DelayCommand.Duration`, `ScaleCommand.To`…). `Reset()` clears it before it goes back to the pool. |
| `ICommandProcessor<TCommand>` | Does the work of one command instance for one run: `Start`, then `Tick` every frame while it reports `Running`, then `Cleanup`. |
| `CommandGroup` | A command holding other commands, run in a `GroupMode` (`Sequential` or `Parallel`), optionally `Repeat`ed. |
| `CommandExecutor` | Owns the registrations (command type → processor type), the pools and the running trees; ticked by the player loop. |
| `CommandBuilder` | Fluent API that rents pooled commands and assembles a tree. |
| `CommandHandle` | A versioned reference to a run: `IsRunning`, `Cancel()`, `OnFinished(callback)`. |

Commands are pure data: the runtime state of a run lives in internal nodes and in processors, so the same command
or group may be executing in several runs at the same time.

## Building and running trees

### The builder

```csharp
CommandHandle handle = executor.GetBuilder(GroupMode.Sequential)  // root group
    .JoinDelay(0.5f)                                               // built-in shortcuts
    .JoinCallback(() => Debug.Log("half a second later"))
    .JoinGroup(GroupMode.Parallel, parallel => parallel            // nested group
        .JoinScale(a, Vector3.one, Vector3.zero, 0.3f)
        .JoinScale(b, Vector3.one, Vector3.zero, 0.6f), repeat: 1)
    .Join<LogCommand>(log => log.Message = "any command type")   // any registered command
    .Execute();
```

`Join(out command)` hands the pooled command out instead of taking a delegate, which avoids a closure when the
values come from variables:

```csharp
var builder = executor.GetBuilder(GroupMode.Sequential);
builder.Join(out DelayCommand delay);
delay.Duration = duration;
builder.Execute();
```

| Method | Adds |
|--------|------|
| `Join<TCommand>(Action<TCommand> configure)` | A pooled command configured by the delegate. |
| `Join<TCommand>(out TCommand command)` | A pooled command handed out for configuration; no delegate, so no closure is allocated. |
| `Join(ICommand command)` | An externally owned command: it is used as is and never released to the pool. |
| `JoinGroup(GroupMode mode, Action<CommandBuilder> build, int repeat = 0)` | A nested group. |
| `JoinDelay`, `JoinCallback`, `JoinLog`, `JoinScale` | Shortcuts for the default commands. |

`Execute()` starts the tree and returns its handle; the pooled commands go back to the pool when the run finishes,
however it finishes. `Build()` returns the root without running it — then the tree is yours: execute it as many
times as you like and release it with `executor.ReleaseCommand(root)` when it is no longer needed.

Prefer `Join(out command)` and static lambdas on hot paths: a lambda that captures variables allocates a closure
every time it is created.

### Executing commands directly

| Call | Ownership |
|------|-----------|
| `executor.Execute(command)` | The caller keeps the command or tree; it can be executed again, even concurrently. |
| `executor.Execute(commands, GroupMode.Parallel)` | Runs a list as an ad-hoc group; the caller keeps the list. |
| `executor.ExecuteAndRelease(command, excluded)` | Releases the tree to the pool when the run finishes, except the commands in `excluded`. |
| `executor.Execute<TCommand>(configure)` | Rents one command, configures it, runs it and releases it. |
| `executor.RentCommand<TCommand>()` / `executor.ReleaseCommand(command)` | Manual pooling. `ReleaseCommand` releases a group's children too. |

### Groups

- **Sequential:** children start one after another; the group completes after the last one.
- **Parallel:** all children start in the same frame; the group completes with the longest one.
- **Repeat:** `repeat` is the number of *extra* passes — `repeat: 2` runs the group three times.
  `CommandGroup.REPEAT_FOREVER` (any negative value) repeats until the run is cancelled.
  A pass that completes without consuming a frame (only instant commands) is deferred to the next tick, so an
  endless loop of instant commands runs once per frame instead of freezing the game.

### Serialized trees

`CommandGroup` and the commands are `[Serializable]`, and `CommandGroup.Commands` is a `[SerializeReference]` list,
so a tree can be a serialized field and tweaked in the inspector:

```csharp
[SerializeField] private CommandGroup _intro = new(GroupMode.Sequential, new List<ICommand>
{
    new ScaleCommand { From = Vector3.zero, To = Vector3.one, Duration = 0.3f },
    new DelayCommand { Duration = 0.5f },
    new LogCommand { Message = "Intro finished" },
});

private void Start() => _executor.Execute(_intro); // the component keeps ownership of the tree
```

The package has no inspector UI for adding new elements of a chosen type to a `[SerializeReference]` list: fill
the tree in code (as above) or use a type picker attribute from another package. Serialized data refers to command
types by class, namespace and assembly name, so renaming them breaks existing data.

## Timing

- The global tick runs at the beginning of the `Update` phase of the player loop, before `MonoBehaviour.Update`,
  and passes `Time.deltaTime` (scaled time: a zero `Time.timeScale` pauses delays and lerps).
- `Execute` is synchronous: commands at the head of the tree start inside the call, and instant ones
  (callbacks, logs, zero durations) also complete there. When the whole tree is instant, `Execute` returns an
  already finished handle.
- A command started during a frame receives its first `Tick` on the next frame. When a sequence advances, the next
  command starts in the same frame in which the previous one completed.
- Runs started while the executor is ticking (from a callback, for example) are first ticked on the next frame.
- An executor created during the global tick is first ticked on the next frame.

## Handles, results and errors

```csharp
CommandHandle handle = builder.Execute();

bool isRunning = handle.IsRunning; // false once the run has finished, however it finished
handle.Cancel();                   // no-op on a finished handle
handle.OnFinished(result =>        // invoked once; right away with Completed if the handle is not running
{
    switch (result)
    {
        case CommandResult.Completed: break;
        case CommandResult.Cancelled: break;
        case CommandResult.Faulted: break;
    }
});
```

- Runs are pooled, so handles are versioned: a stale handle never affects a newer run that reuses the same object.
  `default(CommandHandle)` is a finished handle.
- **Cancel** skips the commands that have not started and calls `Cleanup(interrupted: true)` on the running
  processors. Called from inside the run itself (from a callback command, for example), it takes effect as soon as
  the current step returns.
- **Errors:** a run never throws. When a processor throws, the run finishes as `CommandResult.Faulted`, the processors
  still running are interrupted and the exception is logged with `Debug.LogException`. With the UniTask integration an
  awaited run rethrows the exception instead of logging it. Misuse of the API — `null` arguments, a disposed executor —
  still throws.
- `executor.Dispose()` cancels every run and detaches the executor from the player loop.

## Custom commands

A command is a data class with a parameterless constructor and a `Reset` that restores the defaults:

```csharp
[Serializable]
public class MoveCommand : ICommand
{
    [field: SerializeField] public Transform Target { get; set; }
    [field: SerializeField] public Vector3 To { get; set; }
    [field: SerializeField] public float Duration { get; set; }

    public void Reset()
    {
        Target = null;
        To = default;
        Duration = 0f;
    }
}
```

Its processor runs one instance of it:

```csharp
public class MoveCommandProcessor : ICommandProcessor<MoveCommand>
{
    private MoveCommand _command;
    private Vector3 _from;
    private float _elapsed;

    public CommandStatus Start(MoveCommand command)
    {
        _command = command;
        _from = command.Target.position;
        _elapsed = 0f;

        // Performs the first step now; the logic stays in Tick.
        return Tick(0f);
    }

    public CommandStatus Tick(float deltaTime)
    {
        _elapsed += deltaTime;
        var t = _command.Duration > 0f ? Mathf.Clamp01(_elapsed / _command.Duration) : 1f;
        _command.Target.position = Vector3.Lerp(_from, _command.To, t);
        return t < 1f ? CommandStatus.Running : CommandStatus.Completed;
    }

    public void Cleanup(bool interrupted)
    {
        // The processor goes back to the pool: forget the command, it goes back to its own pool.
        _command = null;
    }
}
```

The lifecycle of one run:

1. `Start(command)` performs the first step. An instant command does all its work here and returns
   `CommandStatus.Completed`; `Tick` and `Cleanup` then keep their default implementations and can be omitted.
2. `Tick(deltaTime)` is called once per frame, starting with the next frame, while the processor returns
   `CommandStatus.Running`.
3. `Cleanup(interrupted)` is called exactly once, right before the processor goes back to the pool — also when
   `Start` or `Tick` threw. `interrupted` is `true` when the run was cancelled or faulted while this processor was
   still running: stop sounds, kill tweens, cancel requests here. Exceptions thrown from `Cleanup` are logged.

Processors are pooled per registration: one instance per concurrently running command, reused afterwards, so keep
all per-run state in fields and reset it in `Start` or `Cleanup`. A processor that keeps no per-run state can
implement `IStatelessCommandProcessor`: a single instance then serves every run, including concurrent ones.

```csharp
public class SetActiveCommandProcessor : ICommandProcessor<SetActiveCommand>, IStatelessCommandProcessor
{
    public CommandStatus Start(SetActiveCommand command)
    {
        command.Target.SetActive(command.IsActive);
        return CommandStatus.Completed;
    }
}
```

For interpolations, derive from `LerpCommand<TValue>` and `LerpCommandProcessor<TCommand, TValue>`: they provide
`Context`, `From`, `To`, `Duration` and an optional easing `Curve` (`null` or a curve without keys means linear),
and you only implement `Lerp` and `Apply` — see `ScaleCommand`.

A builder shortcut is an extension method over `Join(out command)`:

```csharp
public static CommandBuilder JoinMove(this CommandBuilder builder, Transform target, Vector3 to, float duration)
{
    builder.Join(out MoveCommand command);
    command.Target = target;
    command.To = to;
    command.Duration = duration;
    return builder;
}
```

## Registration

The executor maps each command type to its processor type. Registrations come from `ICommandRegistrationContext`s
passed to the constructor — later contexts override earlier ones for the same command — or from `AddRegistration`:

```csharp
public class GameCommandRegistrationContext : ICommandRegistrationContext
{
    public IReadOnlyDictionary<Type, Type> Registrations { get; } = new Dictionary<Type, Type>
    {
        { typeof(MoveCommand), typeof(MoveCommandProcessor) },
        { typeof(SetActiveCommand), typeof(SetActiveCommandProcessor) },
    };
}

var executor = new CommandExecutor(null, new ICommandRegistrationContext[]
{
    new DefaultCommandRegistrationContext(), // DelayCommand, CallbackCommand, LogCommand, ScaleCommand
    new GameCommandRegistrationContext(),
});

executor.AddRegistration<MoveCommand, FastMoveCommandProcessor>(); // replaces the processor for MoveCommand
```

Registering the same pair again keeps its pool; registering another processor replaces the pool, and runs that
still hold an old processor return it to the old pool.

## Object factory and dependency injection

Processors are created through `IObjectFactory.TryCreate(Type, out object)`. The default `ActivatorFactory` needs a
parameterless constructor; a custom factory can inject dependencies. When it returns `false`, the executor falls
back to `ActivatorFactory`, so it only has to know the processors that need something:

```csharp
public class ContainerObjectFactory : IObjectFactory
{
    private readonly Func<Type, object> _resolve; // e.g. an adapter over your DI container

    public ContainerObjectFactory(Func<Type, object> resolve)
    {
        _resolve = resolve;
    }

    public bool TryCreate(Type type, out object instance)
    {
        instance = _resolve(type);
        return instance != null;
    }
}

var executor = new CommandExecutor(new ContainerObjectFactory(container.Instantiate), contexts);
executor.SetFactory(otherFactory); // null restores ActivatorFactory
```

## UniTask integration

Add `COMMAND_EXECUTOR_UNITASK_SUPPORT` to *Project Settings → Player → Scripting Define Symbols* (for every
platform you build) to compile `Pragma.CommandExecutor.UniTask`:

```csharp
await executor.Execute(command, cancellationToken);                  // token overloads
await executor.Execute<DelayCommand>(d => d.Duration = 1f, cancellationToken);
await executor.GetBuilder(GroupMode.Sequential).JoinDelay(1f).Execute(cancellationToken);
await handle;                                                        // CommandHandle is awaitable
await handle.ToUniTask(cancellationToken);
```

- Cancelling the token cancels the run. A cancelled run completes the `await` **normally**, so check the token after
  awaiting when the rest of the method must not run.
- A faulted run rethrows its exception from `await`, and the executor does not log it again. One exception: a fault
  that happens synchronously inside `Execute` (for example a missing registration) occurs before the await is
  attached, so it is only logged.
- `AsyncCommandProcessor<TCommand>` adapts a processor written as an `async UniTask` method. The task is polled every
  frame, its completion is observed on the frame after it finishes, and its token is cancelled when the run is
  interrupted.

```csharp
public class LoadLevelCommandProcessor : AsyncCommandProcessor<LoadLevelCommand>
{
    protected override async UniTask Execute(LoadLevelCommand command, CancellationToken cancellationToken)
    {
        await SceneManager.LoadSceneAsync(command.Scene).WithCancellation(cancellationToken);
    }
}
```

## Singleton

Define `COMMAND_EXECUTOR_SINGLETON_ENABLED` to get `CommandExecutor.Singleton`: a lazily created executor with the
default registrations. In the Editor it is disposed when play mode exits.

## Code stripping

The package works with every managed stripping level, High included, without any setup:

- `link.xml` in the package keeps the package assemblies whole.
- `Editor/CommandExecutorLinkerProcessor` generates an additional link.xml for every player build that keeps all
  types implementing `ICommand` or `ICommandProcessor` in the player assemblies — **your own commands and processors
  included**. Processors are created by reflection from the registered types, so without it High stripping removes
  their constructors (and, for a type that is never instantiated in code, most of its methods).

Verified on the stripped assemblies of an Android IL2CPP build with High stripping: nothing of the package, of the
example commands or of the smoke test processors is removed. Your `IObjectFactory` and
`ICommandRegistrationContext` implementations are created by your own code and need nothing.

## Performance

The hot paths make no GC allocations once the pools are warm; `CommandExecutorPerformanceTests` fails otherwise.
Reference numbers from a standalone .NET 8 benchmark of the kernel (Release, one scenario per process) on the
maintainer's machine:

| Scenario | Time |
|----------|------|
| 1000 concurrent trees of 9 commands built with the builder, from start to completion | 1.3 µs per tree |
| The same tree prebuilt once, executed 1000 times concurrently | 0.8 µs per run |
| 10 000 instant trees (callbacks only) | 0.54 µs per tree |
| Ticking 1000 endless loops (sequential or parallel delays) | 62 µs per frame |

Unity's Mono and IL2CPP runtimes are slower than .NET 8 and the Editor slower still; measure your target device.
To profile inside Unity run the `Performance` category tests and open *Window → Analysis → Performance Test Report*.

Tips: prebuild trees that run often (`Build()` once, `Execute` many times); prefer `Join(out command)` and
static lambdas to closures; implement `IStatelessCommandProcessor` for processors without per-run state; implement
`Tick` and `Cleanup` in running processors instead of relying on the interface defaults.

## Examples

Open `Assets/Examples/Scenes/Examples.unity` in this repository (the examples are not part of the package) and enter
play mode. Every example has Play / Cancel buttons:

| Example | Shows |
|---------|-------|
| Sequence & parallel | Builder basics, nested groups, `repeat`, easing curves. |
| Repeat forever & cancel | `CommandGroup.REPEAT_FOREVER`, cancelling mid-step, `CommandResult.Cancelled`. |
| Wait until | A custom running command (`WaitUntilCommand`) and a stateless instant one (`SetActiveCommand`). |
| Error handling | A throwing command faults the run and interrupts its parallel sibling. |
| Serialized tree | A `[SerializeReference]` tree edited in the inspector and executed as caller-owned data. |
| Object factory | A processor with a constructor dependency created by a custom `IObjectFactory`. |
| UniTask | Awaiting runs with a token, `AsyncCommandProcessor`, cancellation semantics. |

Custom commands of the examples live in `Assets/Examples/Commands` together with their registration context and
builder shortcuts.

## Package layout

- `Kernel` — everything the executor needs: commands and groups, the executor and its builder, handles and results,
  processor contracts, the object factory and the internal runtime (`TreeRunner`, nodes, pools, the player loop
  hook). It does not depend on `Essentials`.
- `Essentials` — content on top of the kernel: the default commands with their builder shortcuts, the singleton and
  the optional UniTask integration (its own assembly).
- `Editor` — the link.xml generator.

All folders share the `Pragma.CommandExecutor` namespace.

## Development

This repository is a Unity project: the package is `Assets/PragmaCommandExecutor`.

- `Assets/Tests/Editor` — EditMode tests: functional, UniTask and performance/zero-allocation (category `Performance`).
- `Assets/Tests/Player` — a player smoke test for code stripping: *Tools → Pragma Command Executor → Build Player
  Smoke Test (High Stripping)* builds it for the active platform; the player reports `[Smoke] PASS/FAIL` on screen
  and in its log.
- `AGENTS.md` — architecture invariants and conventions for contributors and AI agents.

## License

MIT, see [LICENSE.md](LICENSE.md).
