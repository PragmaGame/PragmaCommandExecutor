# Custom commands

A custom command is four pieces: the data class, a processor, a registration, and optionally a builder shortcut and a
tree preset node.

## Data
```csharp
[Serializable]
public class SetActiveCommand : ICommand
{
    [field: SerializeField] public GameObject Target { get; set; }
    [field: SerializeField] public bool IsActive { get; set; }

    // Called when the command goes back to the pool: restore every default.
    public void Reset()
    {
        Target = null;
        IsActive = false;
    }
}
```
- Public parameterless constructor (`RentCommand<T>` and the builder use `new T()`).
- `[Serializable]` + `[field: SerializeField]` so it can live in serialized trees; plain properties (delegates) are fine
  for code-only commands.
- Never store run state in the command: the same instance can run in several runs at once.

## Processor contracts
Implement **exactly one**; `AddRegistration` throws for neither or both.

### Stateless — instant commands
```csharp
public class SetActiveCommandProcessor : IStatelessCommandProcessor<SetActiveCommand>
{
    public void Execute(SetActiveCommand command)
    {
        if (command.Target != null)
        {
            command.Target.SetActive(command.IsActive);
        }
    }
}
```
One instance serves every run, concurrent and nested ones included (a callback that executes another command of the
same type re-enters `Execute`). No fields that change per run. The command completes inside `Execute`; throwing
faults the run.

### Stateful — commands that last
```csharp
public class WaitUntilCommandProcessor : IStatefulCommandProcessor<WaitUntilCommand>
{
    private Func<bool> _condition;

    public void Start(WaitUntilCommand command)
    {
        _condition = command.Condition;      // keep what the ticks need; one-off actions go here
    }

    public CommandStatus Tick(float deltaTime)
    {
        return _condition == null || _condition() ? CommandStatus.Completed : CommandStatus.Running;
    }

    public void Cleanup(bool interrupted)
    {
        _condition = null;                   // the processor returns to its pool: drop references
    }
}
```
Lifecycle of one run:
1. `Start(command)` — begin: read starting values, play a sound, keep references.
2. `Tick(0f)` right away in the same frame: a command that is already done completes here, inside `Execute` when it
   is at the head of the tree.
3. `Tick(deltaTime)` once per frame while it returns `Running`. `deltaTime` is the executor's time (scaled/unscaled,
   fixed step in `FixedUpdate`).
4. `Cleanup(interrupted)` exactly once before the processor returns to its pool — also when `Start` or `Tick` threw.
   `interrupted` is true when the run was cancelled or faulted while this processor was running: stop sounds, kill
   tweens, cancel requests. Exceptions from it are logged.

Processors are pooled per registration (one instance per concurrently running command): keep per-run state in fields
and reset it in `Start` or `Cleanup`. `Tick` and `Cleanup` have no default implementations on purpose.

### Async (UniTask define)
```csharp
public class LoadLevelCommandProcessor : AsyncCommandProcessor<LoadLevelCommand>
{
    protected override async UniTask Execute(LoadLevelCommand command, CancellationToken cancellationToken)
    {
        await SceneManager.LoadSceneAsync(command.Scene).WithCancellation(cancellationToken);
    }
}
```

## Context, data and presets
`Command<TContext, TData>` splits a command into a target (`Context`, set per use) and settings (`Data`): the
command's own `[Serializable]` object, or the data of a `CommandPresetConfig<TData>` asset shared by many commands.
The inspector shows `Context`, an *Is Use Preset* toggle and either the own data or the preset field.

```csharp
[Serializable]
public class ShakeData
{
    [field: SerializeField] public float Strength { get; set; } = 1f;
    [field: SerializeField] public float Duration { get; set; } = 0.3f;
}

[Serializable]
public class ShakeCommand : Command<Transform, ShakeData>
{
    // The default creates new data on every release; resetting in place keeps pooled commands allocation free.
    protected override void OnResetData()
    {
        Data.Strength = 1f;
        Data.Duration = 0.3f;
    }
}

[CreateAssetMenu(menuName = "Game/Shake Preset")]
public class ShakePresetConfig : CommandPresetConfig<ShakeData>
{
}
```
- Unity cannot create an asset of a generic type: one non-generic `CommandPresetConfig<TData>` subclass per data type.
- `command.UsePreset(preset)` switches a command to a preset in code; `Reset()` drops the preset *before*
  `OnResetData`, so releasing a command never resets a shared preset.
- `Data` of a command set to use a missing preset throws `InvalidOperationException`, which faults the run.
- A processor reads `command.Context` and `command.Data` in `Start`; keep the data object, read its fields in `Tick`.

## Lerps
`LerpCommand<TContext, TValue> : Command<TContext, LerpData<TValue>>` with `LerpData<TValue>` = `From`, `To`,
`Duration` (default 0.2), `Curve` (`null` or no keys = linear). Derive a command and a processor, implement `Lerp` and
`Apply`:

```csharp
[Serializable]
public class MoveCommand : LerpCommand<Transform, Vector3>
{
}

public class MoveCommandProcessor : LerpCommandProcessor<MoveCommand, Transform, Vector3>
{
    protected override Vector3 Lerp(Vector3 from, Vector3 to, float t) => Vector3.LerpUnclamped(from, to, t);

    protected override void Apply(Transform context, Vector3 value) => context.localPosition = value;
}
```
The base processor evaluates the curve on normalized time, applies the value from the start frame, completes when the
duration elapses and also when the context is a destroyed Unity object. `LerpVector3PresetConfig`
(*Assets → Create → Pragma → Command Executor → Lerp Vector3 Preset*) is a ready preset for every `Vector3` lerp.

## Registration
```csharp
public class GameCommandRegistrationContext : ICommandRegistrationContext
{
    public IReadOnlyDictionary<Type, Type> Registrations { get; } = new Dictionary<Type, Type>
    {
        { typeof(SetActiveCommand), typeof(SetActiveCommandProcessor) },
        { typeof(MoveCommand), typeof(MoveCommandProcessor) },
    };
}
```
Pass it to the executor constructor (a later context overrides an earlier one for the same command) or call
`executor.AddRegistration<MoveCommand, MoveCommandProcessor>()` at runtime. Registering another processor for a command
replaces its pool; runs that still hold an old processor return it to the old pool. An unregistered command faults
its run with `Command processor for type '...' not found`.

## Builder shortcut
```csharp
public static class GameCommandBuilderExtensions
{
    public static CommandBuilder JoinMove(this CommandBuilder builder, Transform target, Vector3 from, Vector3 to, float duration)
    {
        builder.Join(out MoveCommand command);     // pooled, no closure
        command.Context = target;
        command.Data.From = from;
        command.Data.To = to;
        command.Data.Duration = duration;
        return builder;
    }
}
```

## Object factory and dependency injection
Processors are created through `IObjectFactory.TryCreate(Type, out object)`; when the factory returns `false` the
executor falls back to `ActivatorFactory` (public parameterless constructor). A factory only has to know the
processors that need dependencies, and must not throw for the others (an exception faults the run):

```csharp
// Zenject: new ContainerObjectFactory(type => container.Instantiate(type), typeof(LoadLevelCommandProcessor))
// VContainer: register those processors as Transient and pass type => resolver.Resolve(type)
public class ContainerObjectFactory : IObjectFactory
{
    private readonly Func<Type, object> _create;
    private readonly HashSet<Type> _types;

    public ContainerObjectFactory(Func<Type, object> create, params Type[] types)
    {
        _create = create;
        _types = new HashSet<Type>(types);
    }

    public bool TryCreate(Type type, out object instance)
    {
        instance = _types.Contains(type) ? _create(type) : null;
        return instance != null;
    }
}
```
Bind the executor with its factory, its registration contexts (injected as `IEnumerable<ICommandRegistrationContext>`)
and a `CommandExecutorSettings` instance — VContainer ignores default parameter values, so the settings must be
registered. Let the scope own and dispose the executor:

```csharp
builder.RegisterInstance<IObjectFactory>(new ContainerObjectFactory(...));
builder.Register<ICommandRegistrationContext, DefaultCommandRegistrationContext>(Lifetime.Singleton);
builder.Register<ICommandRegistrationContext, GameCommandRegistrationContext>(Lifetime.Singleton);
builder.RegisterInstance(new CommandExecutorSettings());
builder.Register<CommandExecutor>(Lifetime.Scoped).As<ICommandExecutor>();
```
`executor.SetFactory(factory)` swaps the factory later (`null` restores `ActivatorFactory`); processors already in the
pools stay.

## Code stripping
The package keeps its own assemblies whole and generates a link.xml per player build that preserves every type
implementing `ICommand`, `ICommandProcessor` or `ICommandTreePresetNode` in the player assemblies, so High stripping
needs no setup for the game's commands, processors and tree nodes. Factories and registration contexts are created by
game code and need nothing.
