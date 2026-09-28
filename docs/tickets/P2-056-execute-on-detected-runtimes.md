# P2-056 `--execute` and `runtime-diff` run each side on its own runtime
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-053

## Goal
Execution hard-codes the pair:
- `DriverFactory.cs:29,37-38,56-57` fixes a net48 `app.config`, a net10.0 `runtimeconfig.json`, and
  C# 7.3 against Latest;
- `DriverReferences.cs:22-27` fixes the reference folders;
- `ExecutionEnvironment.NeedsWindows` refuses `--execute` off Windows even for net8 against net8.

A net8 "legacy" assembly would be started on the .NET Framework CLR. Drive every choice from the
detected runtimes instead (ADR 0040 decision 3).

## Spec references
ADR 0040 decision 3; ADR 0035; tickets M3-032, M4-009, P1-008.

## Acceptance criteria (all must hold; nothing beyond them)
1. The driver for a side is built for that side's project runtime:
   - `NetFramework` gives an `.exe` with an `app.config` whose `supportedRuntime` sku names that
     version, compiled at C# 7.3;
   - `NetCore` gives a `.dll` with a `runtimeconfig.json` naming its own `net<v>` and framework
     version, with `rollForward: Disable`, compiled at the language version that runtime ships with.
2. `DriverReferences` finds reference assemblies for any detected runtime: the .NET Framework
   reference folder for its version, and `packs/Microsoft.NETCore.App.Ref/<major>.*` for .NET. A
   missing reference pack or runtime makes that side `not-constructible` with reason
   `runtime <tfm> not installed`. It never falls back to another runtime.
3. `ExecutionEnvironment` requires Windows only when some side's runtime is `NetFramework`. The error
   names the project and runtime. `McpExecuteGate` and `RuntimeDiff` use the same check.
4. A same-runtime pair replays and differential-tests both sides on that one runtime, and a
   counterexample still reproduces or not, as before.
5. `tools/runtime-diff` takes `--from <tfm> --to <tfm>`, defaulting to `net48` and `net10.0`.
   `corpus.ps1 -RuntimeDiff` passes the pair's detected runtimes.
6. VERIFICATION-MODEL.md's execution text and ARCHITECTURE.md's execution notes name the detected
   runtimes, not 4.8 and 10.

## Files
`src/Equiv.Frontend.CSharp/Execution/DriverFactory.cs`, `DriverReferences.cs`, `ReplayDriverFactory.cs`,
`DriverSource.cs`, `src/Equiv.Execute/ChildProcessHost.cs` (only if the host needs the runtime, not
just the extension), `src/Equiv.Cli/ExecutionEnvironment.cs`, `src/Equiv.Cli/Mcp/McpExecuteGate.cs`,
`src/Equiv.Execute/RuntimeDiff.cs`, `tools/runtime-diff/**`, `tools/corpus/corpus.ps1`,
`docs/VERIFICATION-MODEL.md`, `docs/ARCHITECTURE.md`, tests.

## Tests
`DriverFactoryTests.FrameworkSideGetsAnExeForItsVersion`, `DriverFactoryTests.CoreSideGetsItsOwnRuntimeConfig`,
`DriverReferencesTests.MissingPack_IsNotConstructible`, `ExecutionEnvironmentTests.CoreOnlyPairNeedsNoWindows`,
`ReplayIntegrationTests.SameRuntimePairReplays` (runs on Linux and Windows),
`RuntimeDiffTests.FromAndToSelectRuntimes`.

## Size guard
Installing runtimes or targeting packs from the tool: stop, that is the user's machine. More than one
new driver template: stop.

## Out of scope
Non-Windows execution of .NET Framework (Mono). Choosing between several runtimes a `netstandard`
project could be hosted on: use the first in `run.properties.runtimes` order and log it as a `Decision:`
line.

## Notes
