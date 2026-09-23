# PragmaCommandExecutor
## Install via git URL :
```
https://github.com/PragmaGame/PragmaCommandExecutor.git?path=Assets/PragmaCommandExecutor
```

## Usage
The executor is ticked from the player loop (beginning of `Update`) and must be disposed when no longer needed.
```csharp
var executor = new CommandExecutor(null, new ICommandRegistrationContext[] { new DefaultCommandRegistrationContext() });

CommandHandle handle = executor.GetBuilder(CommandExecuteFormat.Sequence)
    .JoinScale(transform, Vector3.zero, Vector3.one, 0.3f)
    .JointGroup(CommandExecuteFormat.Parallel, group => group
        .JoinDelay(0.5f)
        .JoinCallback(() => Debug.Log("parallel")), loop: 2)
    .JoinLog("done", LogType.Log)
    .Execute();

handle.OnFinished(result => Debug.Log(result.Outcome));
handle.Cancel();
```
Custom commands are processed by an `ICommandProcessor<TCommand>`: `Start` runs once, `Tick(deltaTime)` is called every frame
while the processor returns `CommandStatus.Running`. Processors are pooled, one instance per run; a processor that keeps
no per-run state can implement `IStatelessCommandProcessor` to be shared by all runs instead.

## UniTask support
Add `COMMAND_EXECUTOR_UNITASK_SUPPORT` to *Player Settings → Scripting Define Symbols* to compile
`Pragma.CommandExecutor.UniTask`. It makes `CommandHandle` awaitable, restores the token based overloads
(`await executor.Execute(command, token)`, `await builder.Execute(token)`) and provides `AsyncCommandProcessor<TCommand>`
for processors written with `async UniTask`.
