# P2-253 Execution bug: a replay or `probe` driver of a Windows Desktop project runs on `Microsoft.NETCore.App` only, so it cannot load `System.Windows.Forms`
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`ReplayDriverFactory` writes every replay driver's `runtimeconfig.json` with `desktop: false`, so a modern side whose
project targets `net<v>-windows` with WinForms or WPF runs on `Microsoft.NETCore.App` alone, and any member that touches
a `System.Windows.Forms` type fails to load (`NotConstructible System.IO.FileNotFoundException`). In P2-245's runs and
re-adjudication: Git Extensions' `CommitInfo.BranchComparer::Compare(string,string)` (P2-212 sample row 22), and
`TextAreaMouseHandler::IsSelectableChar(char)` and `FontContainer::ParseFont(string)`'s EQ007 replays. Worse, a type
whose static constructor touches one fails as an outcome: the modern `AppSettings` reads
`System.Windows.Forms.Application`, so `AppSettings::GetDictionaryDir()`, `::GetGitExtensionsDirectory()` and
`::ReadBoolRegKey(string,bool)` are `reproduced` only because the modern side throws `TypeInitializationException`
(a `probe` of each shows it). The member
drivers of `runtime-diff` already pick the desktop framework when a driver uses a Windows Desktop assembly
(`DriverFactory`, ticket P2-051). When done, a replay or `probe` driver whose project uses the Windows Desktop framework
runs on `Microsoft.WindowsDesktop.App` of its runtime's version, and a desktop runtime that is not installed is
`runtime <tfm>-windows not installed`, with no other runtime in its place (ADR 0040 decision 3). The ADR 0035 window rule
is unchanged: a window receiver or argument stays refused.

## Spec references
ADR 0040 decision 3, ADR 0035 (the 2026-09-29 clarification), ticket P2-051, `DriverRuntime.WriteConfig`,
`ReplayDriverFactory.Driver`, `docs/runs/2026-10-19-execution-reach.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A replay driver of a project that references the Windows Desktop framework writes `Microsoft.WindowsDesktop.App` and
   its version; one that does not is unchanged. Unit tests over both.
2. A modern driver's `TypeInitializationException` or `FileNotFoundException` for a Windows Desktop assembly is
   `not-constructible`, never an outcome a replay compares. Unit test.
3. With `Microsoft.WindowsDesktop.App` 5.0 installed (ask the user first: installing a runtime changes the machine),
   Git Extensions' rows 22 and 41 are probed again and re-adjudicated, and the three `AppSettings` replays rerun; without
   it, the reason names the missing runtime.

## Notes
- Filed 2026-10-19 by P2-245. The box has `Microsoft.WindowsDesktop.App` 10.x only; P2-237 installed the .NET 5.0.17
  runtime without the desktop one.
- Decision: a project uses the Windows Desktop framework when a reference of its compilation sits in a
  `Microsoft.WindowsDesktop.App.Ref` folder, as the SDK resolves `UseWindowsForms`/`UseWPF` on `net<v>-windows` and
  flows a framework reference to referencing projects (`ReplayDriverFactory.UsesDesktop`). Only a .NET host is
  affected; a .NET Framework driver's `app.config` is unchanged. A missing desktop runtime is
  `runtime net<v>-windows not installed` (`DriverFactory.NotInstalled(runtime, desktop: true)`), checked before the
  project is emitted.
- Decision: "a Windows Desktop assembly" in criterion 2 is a fixed list in the driver template (`L.Desktop`): the
  managed assemblies of `Microsoft.WindowsDesktop.App` 10.0 that `Microsoft.NETCore.App` lacks. The driver walks the
  exception's inner chain for a `FileNotFoundException` or `FileLoadException` whose `FileName` names one, and answers
  `NotConstructible "<exception type> loading <assembly>"`; any other missing file stays `Threw`. The template is shared,
  so `runtime-diff`'s member drivers and both sides get it too; it can only turn a `Threw` into `NotConstructible`.
- Decision: criterion 3's runtime was not installed machine-wide. .NET 5 is out of support (2022-05-10), and the user
  allowed an install only if leaving it would not be a latent risk, which an unserviced runtime that any
  `net5.0-windows` application would use is. Instead a private dotnet root in the session scratch folder (a copy of
  `dotnet.exe`, junctions to the machine install, and `Microsoft.WindowsDesktop.App` 5.0.17 from Microsoft's zip,
  every DLL's Microsoft Authenticode signature checked) ran `equiv` and its drivers through `PATH` and `DOTNET_ROOT`.
  `C:\Program Files\dotnet` is unchanged. `docs/runs/2026-10-19-desktop-replay.md` records the setup.
- Criterion 3: `full --execute` of Git Extensions (353 s). The five replays P2-245's missing desktop runtime decided
  now run: `ReadBoolRegKey` is `not-applicable` (both `false`), `IsSelectableChar` `not-applicable`, the two `ParseFont`
  and `GetPathToSparseCheckoutFile` `not-constructible` for split call traces. `GetDictionaryDir` and
  `GetGitExtensionsDirectory` stay `reproduced`, now because `Application.ExecutablePath` is the legacy driver on one
  side and the `dotnet` host on the other: filed P2-255. Rows 22 and 41 probed again on P2-245's inputs: both
  **confirmed** (22: -1 against 31 and 131, culture-aware against ordinal; 41: `recovery.txt` against
  `recovery.json`/`.html`, ICU ignoring a leading NUL or zero-width space). Migration Divergent precision over P2-245's
  sample: 51.9% (41 of 79), from 50.6%; ceiling unchanged at 68.1%; scoreboard not changed (P2-247 refreshes it).
