# P2-056 `--execute` and `runtime-diff` run each side on its own runtime
Status: done (PR #316)
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
- Decision: `DriverRuntime` (frontend-internal, new) says how a driver runs on one `TargetRuntime`: extension, C# version, and the `app.config` or `runtimeconfig.json` it writes. `DriverReferences` becomes the locator for any runtime: `Host(runtime)` (the installed runtime only, for replay drivers, which compile against their project's own references) and `For(runtime)` (plus reference assemblies, for member drivers). One driver template stays (`DriverSource`).
- Decision: `rollForward: Disable` forbids even patch roll-forward, so `runtimeconfig.json` names the exact installed shared framework version: the newest numbered `shared/Microsoft.NETCore.App/<major.minor>.*` of the `dotnet` install equiv runs on (previews ignored), and likewise `Microsoft.WindowsDesktop.App` for a desktop driver.
- Decision: the .NET reference pack is matched on `<major.minor>.*`, not the ticket's `<major>.*`, so `netcoreapp3.0` and `netcoreapp3.1` never take each other's pack; for net5.0 and later the two are the same.
- Decision: a .NET Framework 4.x runtime counts as installed when `%WINDIR%\Microsoft.NET\Framework\v4.0.30319` exists (every 4.x runs on that CLR); a member driver also needs the targeting pack `v<version>` with its `FrameworkList.xml`.
- Decision: C# version by runtime: .NET Framework and .NET Core 2.x 7.3, 3.x 8, net5 9, net6 10, net7 11, net8 12, net9 13, net10 14, anything newer `Latest`.
- Decision: a project hosted on several runtimes replays on the first in `run.properties.runtimes` order (runtime order); an unhosted project, which has none, makes its side `not-constructible` with `the <side> project <name> has no detected runtime` (there is no tfm to call "not installed").
- Decision: in `runtime-diff`, a runtime that is not installed makes `Resolve` return one signature for the `--member` text whose only not-constructible reason is `runtime <tfm> not installed`, so the tool reports it and exits 0; `Create` throws the same reason.
- Decision: the one Windows check is `Equiv.Execute.WindowsRequirement.Refusal` (public, since `Equiv.Execute` cannot see `Equiv.Cli`); `ExecutionEnvironment.Refusal(analysis)` wraps it for `compare` and `probe`, `RuntimeDiff` calls it with `--from`/`--to`. Message: `<project> runs on <tfm>, and .NET Framework needs Windows (ADR 0040)`, prefixed `error: --execute: ` or `runtime-diff: `.
- Decision: `compare --execute` checks after loading, since the runtimes come from the loaded projects, so off Windows a framework pair now loads before exit 3. `equiv mcp --execute` no longer refuses at startup (no solution is known there): it always prints the note and registers `probe`, and `probe` refuses a framework pair per call.
- Decision: `runtime-diff`'s report format is unchanged (no `from`/`to` fields); criterion 5 asks only for the flags.
- Decision: `corpus.ps1 -RuntimeDiff` passes, per side, the runtime most of that side's projects run on in the census SARIF's `run.properties.runtimes` (a hosted project counts its first; `netstandard` and `unknown` count for nothing); a census from before P2-053 keeps `runtime-diff`'s defaults.
- Deviation: `ReplayIntegrationTests.SameRuntimePairReplays` lives in `Equiv.Frontend.CSharp.Tests` (which now references `Equiv.Execute`), not `Equiv.Tests.Integration`, because CI runs the integration project on Windows only and the ticket wants it on Linux too. It builds two projects over the test host's runtime, locates that runtime as `--execute` does, and runs both drivers through the real `ChildProcessHost`.
- Deviation: README's `--execute` and `probe` paragraphs said both need Windows unconditionally; corrected, since this ticket made that false (README is otherwise P2-057's).
