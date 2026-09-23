# PragmaCommandExecutor
## Install via git URL :
```
https://github.com/PragmaGame/PragmaCommandExecutor.git?path=Assets/PragmaCommandExecutor
```

## Usage
The executor is ticked from the player loop (beginning of `Update`) and must be disposed when no longer needed.
```csharp
var executor = new CommandExecutor(null, new ICommandRegistrationContext[] { new DefaultCommandRegistrationContext() });

CommandHandle handle = executor.GetBuilder(GroupMode.Sequential)
    .JoinScale(transform, Vector3.zero, Vector3.one, 0.3f)
    .JoinGroup(GroupMode.Parallel, group => group
        .JoinDelay(0.5f)
        .JoinCallback(() => Debug.Log("parallel")), repeat: 2)
    .JoinLog("done", LogType.Log)
    .Execute();

handle.OnFinished(result => Debug.Log(result.Outcome));
handle.Cancel();
```
`repeat` is the number of extra passes: `repeat: 2` runs the group three times, `CommandGroup.RepeatForever` until cancelled.

Custom commands are processed by an `ICommandProcessor<TCommand>`: `Start` runs once, `Tick(deltaTime)` is called every frame
while the processor returns `CommandStatus.Running`. Processors are pooled, one instance per run; a processor that keeps
no per-run state can implement `IStatelessCommandProcessor` to be shared by all runs instead.

## UniTask support
Add `COMMAND_EXECUTOR_UNITASK_SUPPORT` to *Player Settings → Scripting Define Symbols* to compile
`Pragma.CommandExecutor.UniTask`. It makes `CommandHandle` awaitable, restores the token based overloads
(`await executor.Execute(command, token)`, `await builder.Execute(token)`) and provides `AsyncCommandProcessor<TCommand>`
for processors written with `async UniTask`.

## Package layout
- `Kernel` — everything the executor needs to run: commands and groups, the executor and its builder, processor contracts,
  pooling and the internal runtime (`TreeRunner` and its nodes). It does not depend on `Essentials`.
- `Essentials` — ready-made content on top of the kernel: default commands with their builder shortcuts
  (`JoinDelay`, `JoinCallback`, `JoinLog`, `JoinScale`), `CommandExecutor.Singleton` and the optional UniTask integration.

Both folders share the `Pragma.CommandExecutor` namespace. Tests live outside the package in `Assets/Tests`.
