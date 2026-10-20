# P2-259 Execution bug: a .NET Framework replay or `probe` driver runs with the compatibility quirks of an application that targets no framework, so extended paths fail where the solution's own host accepts them
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
.NET Framework 4.x picks its `AppContext` compatibility defaults from the entry assembly's `TargetFrameworkAttribute`.
A replay or `probe` driver for a .NET Framework side is an executable with an `app.config` naming the project's sku and
no such attribute, so it runs with the defaults of an application that targets no framework, not those of the project.
P2-247 measured both on .NET Framework 4.8 with a two-line program built twice
(`docs/runs/2026-10-20-precision-execute-verdict-2.md`): against an application built for 4.7.1 with Duplicati's own
`UseLegacyPathHandling=false`, the driver-like build has `Switch.System.IO.UseLegacyPathHandling` true,
`Switch.System.IO.BlockLongPaths` true, `Switch.System.Globalization.NoAsyncCurrentCulture` true and
`Switch.System.Security.ClaimsIdentity.SetActorAsReferenceWhenCopyingClaimsIdentity` true, where the solution has all
four false or unset. Under legacy path handling every `\\?\` extended path throws `ArgumentException`, a legal one
included, so each `SystemIOWindows` member of `duplicati-3124` (which prefixes every path with `\\?\`) throws on the
legacy driver and returns on the solution's host: P2-247's run has 9 `reproduced` replays from it (EQ002
`SetAccessControlFile`, `SetAccessControlDir`; EQ006 `DirectoryCreate`, `DirectoryDelete`, `FileDelete`, `FileCreate`,
`GetFileAttributes`, `PathGetFileName`, `PathGetFullPath`), none showing the claimed difference. Two of P2-237's
confirmations rested on it (`SystemIOWindows::GetFileAttributes(string)` and `::PathChangeExtension(string,string)`):
run as Duplicati's hosts run, against a net5.0 build on 5.0.17, both sides agree on every input, so P2-247 reclassified
them as false positives. An ordinary path holding a character .NET Framework rejects throws `ArgumentException` in both
builds, through every API P2-237, P2-245 and P2-247 confirmed with, so no other confirmation rests on the quirk.
When done, a .NET Framework driver runs with the compatibility defaults of the project
it drives (its target framework, and the switches its own configuration sets), and a harness quirk never reaches
`reproduced` (ADR 0035 decision 2).

## Spec references
ADR 0035 decision 2, ADR 0040 decision 3, `src/Equiv.Frontend.CSharp/Execution/DriverRuntime.cs` (`WriteConfig`),
`ReplayDriverFactory`, `DriverFactory`, tickets P2-253, P2-254 and P2-255 (harness differences that reached
`reproduced`), `docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A .NET Framework driver carries the target framework of the project it drives (the driver assembly's
   `TargetFrameworkAttribute`, or the `app.config` `AppContextSwitchOverrides` that framework implies), and the switches
   the project's own `app.config` sets under `runtime/AppContextSwitchOverrides`; a unit test shows both for a project
   that sets `UseLegacyPathHandling=false`.
2. An integration test against a `samples/` pair whose member calls `Path.GetFullPath` on a `\\?\` path gives equal
   outcomes under `--execute` on .NET Framework 4.8 and .NET 10 (no `reproduced` replay).
3. `duplicati-3124` in `full --execute`: the 9 replays above are each given with their value now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247 (its Duplicati sub-agent's finding, confirmed outside equiv with a program built twice).
