---
name: unity-verify
description: "Compile this Unity project and run its EditMode, Graph Toolkit and PlayMode tests in the running Editor from the terminal, and get past the Editor states that block it (closed Editor, port clashes, modal dialogs, Safe Mode). Use after changing code in this repository and before committing."
---
# unity-verify

The project has `com.unity.pipeline`, which exposes the running Editor to the `unity` CLI
(`%LOCALAPPDATA%\Unity\bin\unity`). The scripts below run from any directory of the repository.

## Steps
1. `unity --no-banner status` — this project must be listed `ready`. Several Editors may run on the machine (each on
   its own port); the CLI picks this project's one from `Library/Pipeline/.unity-pipeline-port`. Not listed:
   `unity --no-banner --non-interactive open .` from the repository root (it returns before the Editor is up; a cold
   import of the project takes about a minute), then poll `status`.
2. `.claude/skills/unity-verify/scripts/compile.sh` — imports changed files (the Editor does not import while it is
   unfocused), recompiles and prints `completed False` or the compile errors. Exit code 1 on errors.
3. `.claude/skills/unity-verify/scripts/test.sh [editor|graph|playmode|all]` — runs `Pragma.CommandExecutor.Tests`,
   `Pragma.CommandExecutor.GraphToolkit.Editor.Tests` and `Pragma.CommandExecutor.PlayModeTests`; prints the summary of
   each and every failure with its message. Exit code 1 if anything failed or nothing ran.

## When the Editor does not answer
- Every main thread command times out (`Main thread operation timed out`, `editor_status` slow): a modal dialog is
  open. `powershell -ExecutionPolicy Bypass -File .claude/skills/unity-verify/scripts/dialogs.ps1` lists this Editor's
  windows and dialog buttons; `-Click "<button>"` presses one. Typical dialogs:
  - *The open scene(s) have been modified externally* — after editing an open `.unity` file on disk: `Reload` keeps
    the file's version.
  - *Enter Safe Mode?* — the project does not compile at startup: `Quit`, fix the errors, open the project again
    (`Ignore` leaves the Editor without the pipeline server).
  - *Unity Package Manager Error* — an invalid `Packages/manifest.json`: fix it, then `Retry`.
  - *Unsupported Input Handling on Android* during an Android player build: `Ignore`.
  Never take desktop screenshots to find dialogs: the user's windows are on the same desktop.
- `401 Unauthorized` or another project's path in `editor_status`: two Editors share a port (an old
  `com.unity.pipeline` bound `0.0.0.0`). Check `netstat -ano | grep ":78"` and `unity status`; the project uses
  `com.unity.pipeline` 0.8.0-exp.1+, which picks a free port.
- `recompile` answers `up_to_date` when the Editor already compiled the change on its own: trust
  `recompile_status` (what `compile.sh` prints) or the timestamps of `Library/ScriptAssemblies/Pragma.CommandExecutor*.dll`.
  The console (`unity command console`) keeps entries of earlier compiles: never read errors from it alone.
- PlayMode tests cannot run synchronously over HTTP (entering play mode reloads the domain): `test.sh playmode` starts
  them with `--async_tests true` and polls `Temp/pipeline_test_status.json`.
- Play mode stops advancing after an exception was logged (`editor_status` reports `"playMode": "paused"`): the
  Console's *Error Pause* is on. `unity command editor_pause` toggles the pause; repeat after every expected exception.

## Other checks
- Editor C# without a domain reload: `unity --no-banner --json command run_script --file <abs path .cs> --entry Type.Method`
  (all project assemblies are referenced, internals are not: use reflection). Good for building scenes or graphs and
  for compiling code snippets of the docs against the real API.
- Play mode by hand: `unity command editor_play`, poll `editor_status` for `"playMode": "playing"`, `run_script` to
  drive objects, `unity command editor_stop`.
- Unity Performance Test timings on this machine swing between sessions because other Editors share it: do not judge
  a change by them. The zero-allocation tests (`Is.Not.AllocatingGCMemory`) are deterministic and must pass.

## Code stripping check
After changing anything reflection related (processor creation, serialized node or command types, the link.xml
generator):
1. Open `Assets/Tests/Player/PlayerSmokeTest.unity` and run it in play mode first: it logs `[Smoke] PASS/FAIL` lines
   and `[Smoke] RESULT n/n passed` (unpause after its expected exceptions, see above).
2. Build it: `unity command --timeout 5 menu --path "Tools/Pragma Command Executor/Build Player Smoke Test (High Stripping)"`
   (the request times out, the build goes on). Answer *Unsupported Input Handling on Android* with
   `dialogs.ps1 -Click "Ignore"`; progress windows (*Compiling Scripts*, *Cancel / Skip Transcoding*) are not questions.
   Done when `Logs/Editor.log` has `Player smoke test build: Succeeded`. The menu then opens Explorer on the output.
3. `.claude/skills/unity-verify/scripts/strip-check.sh [Android]` compares every `Pragma.CommandExecutor*` player
   assembly with its stripped copy (`Library/Bee/artifacts/<Platform>/ManagedStripped`); it needs the .NET 10 SDK. Every
   assembly must report `missing 0`. Running the APK needs a device: the player prints the same `[Smoke]` lines.
4. Afterwards `git status` may show `managedStrippingLevel: Android: 4` added to `ProjectSettings.asset` — the restored
   default written out explicitly: revert it. `.utmp/` is native build temp and is ignored.
