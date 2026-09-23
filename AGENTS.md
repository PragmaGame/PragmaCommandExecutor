# AGENTS.md

Guidance for AI agents (and humans) working in this repository.

## What this is

The development project of the Unity package `com.pragma.commandexecutor` — a tick based command executor.
The package itself is `Assets/PragmaCommandExecutor` and is consumed through a git URL with
`?path=Assets/PragmaCommandExecutor`; everything else in the project (tests, examples, settings) is not shipped.
Editor version: see `ProjectSettings/ProjectVersion.txt` (developed on Unity 6000.3).

The package is pre-1.0: backward compatibility is not required yet, prefer clean renames over compatibility shims.

## Layout

| Path | Contents |
|------|----------|
| `Assets/PragmaCommandExecutor/Kernel` | Everything the executor needs: `ICommand`, `CommandGroup`, `GroupMode`, `CommandExecutor`, `CommandBuilder`, `CommandHandle`, `CommandResult`, processor contracts (`Processors/`), `IObjectFactory` (`Factory/`), and the internal runtime (`Execution/`: `TreeRunner`, `CommandNode`, `ProcessorPool`, `CommandExecutorPlayerLoop`). |
| `Assets/PragmaCommandExecutor/Essentials` | Content on top of the kernel: default commands (`Commands/`), builder shortcuts, `CommandExecutor.Singleton`, the optional UniTask integration (`UniTask/`, own asmdef). |
| `Assets/PragmaCommandExecutor/Editor` | Editor-only code shipped with the package: `CommandExecutorLinkerProcessor` (generated link.xml). |
| `Assets/PragmaCommandExecutor/link.xml` | Keeps the package assemblies whole under managed code stripping. |
| `Assets/Tests/Editor` | EditMode tests: functional (`CommandExecutorTests`), UniTask (`CommandExecutorUniTaskTests`), performance and zero-allocation (`CommandExecutorPerformanceTests`, category `Performance`). |
| `Assets/Tests/Player` | Player smoke test (`PlayerSmokeTest` scene + script) and its build menu item for checking code stripping. |
| `Assets/Examples` | Example scene and scripts; they double as the "user code" that the stripping checks run against. |

## Architecture invariants

- **Kernel never references Essentials.** Both live in one assembly, so the compiler does not enforce it: check that
  `Kernel/**/*.cs` + `AssemblyInfo.cs` compile on their own after touching the kernel.
- **One namespace, `Pragma.CommandExecutor`, for every folder** (including Editor code). Serialized
  `[SerializeReference]` data stores the class name, namespace and assembly of each command: renaming or moving a
  command type, its namespace or its assembly breaks existing scenes and assets.
- **Commands are pure data.** All runtime state lives in nodes (`CommandNode`) and processors, so the same command
  or group may be executing in several runs at once. Never store run state in a command.
- **Processor contract** (`ICommandProcessor<TCommand>`): `Start(command)` performs the first step and returns a
  `CommandStatus`; `Tick(deltaTime)` runs once per frame from the next frame while `Running`; `Cleanup(interrupted)`
  runs exactly once per run before the processor returns to its pool (`interrupted` is true only for processors still
  running when the run was cancelled or faulted). `IStatelessCommandProcessor` shares one instance across all runs.
- **Runs never throw.** A processor exception finishes the run as `CommandResult.Faulted` and is logged exactly once
  by `TreeRunner`; only the UniTask integration rethrows it from `await` (and then it is not logged). API misuse
  (null arguments, a disposed executor) still throws.
- **Execution is synchronous at start:** `Execute` runs instant commands at the head of the tree inside the call;
  a command started during a step gets its first `Tick` on the next frame. Several tests pin this timing.
- **Zero GC allocations on the hot paths** once pools are warm: ticking, executing prebuilt trees and building trees
  with `CommandBuilder`. `CommandExecutorPerformanceTests` enforces it with `Is.Not.AllocatingGCMemory()`.
  Unity's Mono BCL allocates where .NET does not — e.g. `HashSet<T>.UnionWith(IEnumerable<T>)` boxes the enumerator —
  so a .NET benchmark alone does not prove zero allocations; run the Unity tests.

## Performance rule

Performance is a primary goal. For any change on the hot path (`TreeRunner`, `CommandNode`, `ProcessorPool`,
`CommandExecutor` tick/launch/pools, processor dispatch):

1. Benchmark the new code against the previous one on the same machine. Unity Editor timings
   (*Window → Analysis → Performance Test Report* after running the `Performance` tests) are only comparable with
   each other. A standalone .NET 8 benchmark over the kernel sources compiled with `-optimize+` is more precise: run each
   scenario in its own process with `DOTNET_TieredCompilation=0`, 5–7 runs, compare min and median
   (running all scenarios in one process gives misleading results).
2. Anything slower than noise (~1–2%) is a regression: find the cause or roll the change back.
3. Known pitfall: calling a default interface method of a generic interface is slower than calling a class method,
   so running processors implement `Tick` and `Cleanup` themselves instead of relying on the interface defaults.

## Code stripping

Processors are created with `Activator.CreateInstance` from registered types, which the linker cannot see.
`Editor/CommandExecutorLinkerProcessor.cs` generates a link.xml per build that preserves every non-interface type
implementing `ICommandProcessor` or `ICommand` in the player assemblies; `link.xml` preserves the package assemblies.
After changing anything reflection related, build the player smoke test (below) and make sure it passes, or at
least compare the stripped assemblies in `Library/Bee/artifacts/<Platform>/ManagedStripped` with
`Library/Bee/PlayerScriptAssemblies`.

## Style

- C#: 4 spaces, braces on their own lines and always present, block bodied methods (expression bodies only for
  trivial one-liners), `var` for locals, `_camelCase` private fields, `PascalCase` members,
  **UPPER_SNAKE_CASE constants** (`CommandGroup.REPEAT_FOREVER`).
- Code, comments and docs are in English. Comments explain *why*; public API gets XML docs.
- Files use CRLF in the working tree. Keep an existing BOM, add none to new files.
- Every asset has a `.meta` file that must be committed. Move or rename files together with their `.meta`
  (`git mv` both) so GUIDs survive; never edit GUIDs by hand.
- Keep `README.md` in sync with public API changes.

## Working with the Unity Editor

The project has `com.unity.pipeline`, which exposes the running Editor to the `unity` CLI
(`%LOCALAPPDATA%\Unity\bin\unity`), auto-discovered from the project directory:

```bash
unity --no-banner --json command editor_status                         # must report "ready"
unity --no-banner --json command menu --path "Assets/Refresh"          # import changed files while the Editor is unfocused
unity --no-banner --json command recompile                              # then poll recompile_status
unity --no-banner --json command recompile_status                       # data.result is a JSON *string*: parse it
unity --no-banner --json command console                                # compile errors and logs
unity --no-banner --json command --timeout 600 run_tests --mode editor --filter_type assembly --filter Pragma.CommandExecutor.Tests
unity --no-banner --json command run_script --file <file.cs> --entry Type.Method   # run editor C# without a domain reload
```

`recompile` answering `up_to_date` may mean the Editor already compiled the change on its own: compare the
timestamps in `Library/ScriptAssemblies/Pragma.CommandExecutor*.dll`.

Player smoke test: *Tools → Pragma Command Executor → Build Player Smoke Test (High Stripping)* builds
`Assets/Tests/Player/PlayerSmokeTest.unity` for the active target (Android in this project) and restores the stripping
level afterwards. The player shows `[Smoke] PASS/FAIL` lines on screen and in its log; in batch mode it exits with
code 0 only when every check passed.

## Tests

- New behaviour gets an EditMode test in `CommandExecutorTests`. Use the `autoTick: false` internal constructor and
  drive frames with the internal `Tick`; `ProbeCommand` records the processor lifecycle (`start`, `end`, `cleanup`,
  `interrupt`) into a log for exact assertions.
- Name tests `Subject_ExpectedBehaviour`. Expected `Debug.LogException` output must be declared with `LogAssert.Expect`,
  otherwise Unity fails the test.
- Allocation and timing checks go to `CommandExecutorPerformanceTests`.

## Git

Do not commit or push unless asked: the maintainer reviews and commits changes. The maintainer prefers answers in
Russian; everything in the repository stays in English.
