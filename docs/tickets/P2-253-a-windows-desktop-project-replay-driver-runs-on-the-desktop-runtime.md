# P2-253 Execution bug: a replay or `probe` driver of a Windows Desktop project runs on `Microsoft.NETCore.App` only, so it cannot load `System.Windows.Forms`
Status: todo
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
