# Using the executor

## Builder
`executor.GetBuilder(GroupMode mode)` returns a `CommandBuilder` struct whose root is a pooled `CommandGroup`.

| Method | Adds |
|--------|------|
| `Join<T>(Action<T> configure)` | A pooled `T` configured by the delegate (a capturing lambda allocates a closure). |
| `Join<T>(out T command)` | A pooled `T` handed out for configuration; no delegate. Use it on hot paths and in shortcuts. |
| `Join(ICommand command)` | An externally owned command: used as is, never released to the pool. |
| `JoinGroup(GroupMode mode, Action<CommandBuilder> build, int repeat = 0)` | A nested group. |
| `JoinDelay(seconds)`, `JoinCallback(action)`, `JoinLog(message, logType)`, `JoinScale(transform, from, to, duration, curve = null)`, `JoinScale(transform, preset)` | Shortcuts for the default commands. |

- `Execute()` starts the tree and returns its `CommandHandle`; the pooled commands return to the pool when the run
  finishes, however it finishes. The builder must not be used after `Execute()` or `Build()`.
- `Build()` returns the root without running it: the tree is the caller's, execute it any number of times (even
  concurrently) and give it back with `executor.ReleaseCommand(root, excluded)` when done.
- Always finish a builder with `Execute()` or `Build()`: an abandoned one never returns its pooled lists and rented
  commands to the pools.

## Executing
| Call | Ownership |
|------|-----------|
| `Execute(ICommand command)` | Caller keeps the command or tree; never released. |
| `Execute(IReadOnlyList<ICommand> commands, GroupMode mode)` | Runs a list as an ad-hoc group; caller keeps the list. |
| `ExecuteAndRelease(ICommand command, HashSet<ICommand> excluded = null)` | Releases the tree to the pools when the run finishes, except `excluded` (copied, reusable right away). |
| `Execute<T>(Action<T> configure)` (extension) | Rents one `T`, configures, runs and releases it. |
| `RentCommand<T>()` / `ReleaseCommand(command, excluded)` | Manual pooling; releasing a group releases its children. `ReleaseCommand` calls `Reset()`. |

`ExecuteAndRelease` accepts commands created with `new` (e.g. `CommandTreePreset.Build` results): they simply join
the pools.

## Groups and timing
- **Sequential:** children start one after another; **Parallel:** all start in the same frame and the group ends with
  the longest one.
- `Repeat` is the number of *extra* passes; `CommandGroup.REPEAT_FOREVER` (any negative) repeats until cancelled. A
  pass that consumed no frame (only instant commands) is deferred to the next tick, so an endless loop of instant
  commands runs once per frame.
- `Execute` starts the head of the tree inside the call. A stateful command takes its first step there
  (`Start` + `Tick(0)`) and its next `Tick` on the next frame. When a sequence advances, the next command starts in
  the frame the previous one completed.
- Runs started during the executor's tick (from a callback) are first ticked on the next frame; an executor created
  during the global tick is first ticked on the next frame.
- The global tick runs at the beginning of the configured player loop phase (default `Update`, before
  `MonoBehaviour.Update`). `FixedUpdate` ticks once per fixed step with the fixed time step.

## Executor settings
`CommandExecutorSettings` (`[Serializable]`, copied by the constructor):

| Property | Default | Meaning |
|----------|---------|---------|
| `IsAutoTicked` | `true` | `false`: only `executor.Tick(deltaTime)` advances runs; `Tick` on an auto-ticked executor throws. |
| `TimeMode` | `Scaled` | `Unscaled` keeps running while `Time.timeScale` is 0 (pause menus). |
| `UpdatePhase` | `Update` | `EarlyUpdate`, `FixedUpdate`, `PreUpdate`, `Update`, `PreLateUpdate`, `PostLateUpdate`. |

Use one executor per time domain: e.g. an unscaled executor for UI and a scaled one for gameplay.
`Tick` is not re-entrant: calling it from a command of the same executor throws inside that command, which faults its run.

## Handles, results, errors
- `CommandHandle` is versioned: once its run finishes it stops `IsRunning` and every call on it is a no-op, even
  after the run object is reused. `default(CommandHandle)` is a finished handle — safe to `Cancel()`.
- `OnFinished(callback)` is invoked once with `Completed`, `Cancelled` or `Faulted`; on a finished handle right away
  with `Completed` (the real result of a finished run is not kept).
- **Cancel** skips commands that have not started and calls `Cleanup(interrupted: true)` on the running processors.
  From inside the run it takes effect when the current step returns.
- **Faults:** a processor exception finishes the run as `Faulted`, interrupts the processors still running and is
  logged once with `Debug.LogException`. Exceptions from `Cleanup` and from `OnFinished` listeners are logged too.
- `Dispose()` cancels every run and detaches the executor; `Execute` afterwards throws `ObjectDisposedException`.

## UniTask (`COMMAND_EXECUTOR_UNITASK_SUPPORT`)
```csharp
await executor.Execute(command, token);
await executor.Execute(commands, GroupMode.Parallel, token);
await executor.Execute<DelayCommand>(d => d.Duration = 1f, token);
await executor.GetBuilder(GroupMode.Sequential).JoinDelay(1f).Execute(token);
await handle;                         // CommandHandle is awaitable
await handle.ToUniTask(token);
```
- Cancelling the token cancels the run and completes the await **normally**; an already cancelled token starts
  nothing (the builder overload releases the built tree).
- A faulted run rethrows from `await` and is then not logged. A fault inside the synchronous start (e.g. an
  unregistered command at the head of the tree) happens before the await attaches: it is only logged.
- `AsyncCommandProcessor<T>` adapts an `async UniTask Execute(command, token)` method into a stateful processor: polled
  every frame from the start frame, completion observed on the frame after the task ends, token cancelled on interrupt.

## Serialized trees
`CommandGroup` and the built-in commands are `[Serializable]`; `CommandGroup.Commands` is `[SerializeReference]`.
A `[SerializeField] CommandGroup _tree = new(GroupMode.Sequential, new List<ICommand> { ... })` field is edited in the
inspector and run with `Execute(_tree)` (caller-owned, never released). The package has no type picker for adding
elements to `[SerializeReference]` lists: fill the tree in the field initializer, use a picker from another package,
or author a tree preset graph instead (tree-presets.md).

## Testing
```csharp
[SetUp]
public void SetUp()
{
    _executor = new CommandExecutor(null,
        new ICommandRegistrationContext[] { new DefaultCommandRegistrationContext(), new GameCommandRegistrationContext() },
        new CommandExecutorSettings { IsAutoTicked = false });
}

[TearDown]
public void TearDown() => _executor.Dispose();

[Test]
public void Door_OpensOverTwoFrames()
{
    var handle = _executor.Execute(tree);
    _executor.Tick(0.25f);                // 0.25 is exact in binary: durations never drift
    _executor.Tick(0.25f);
    Assert.IsFalse(handle.IsRunning);
}
```
Expected `Debug.LogException` output of faulted runs must be declared with `LogAssert.Expect(LogType.Exception, ...)`.
Auto-ticked executors only tick in play mode: test the player loop in PlayMode tests.
