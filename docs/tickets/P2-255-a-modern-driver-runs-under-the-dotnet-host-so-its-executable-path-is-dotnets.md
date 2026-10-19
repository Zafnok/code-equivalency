# P2-255 Execution bug: a modern replay or `probe` driver runs as `dotnet <driver>.dll`, so an outcome that holds the process's executable path is `reproduced` on any runtime pair
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`ChildProcessHost.Start` runs a .NET Framework driver as its own `EquivReplay<n>.exe` and a .NET driver through
`dotnet`, so the two sides' processes have different executables in different folders: the legacy one is
`<root>\legacy\<project>\EquivReplay<n>.exe`, the modern one the `dotnet.exe` of the install that runs it. An outcome
that holds the process's executable path then differs on any runtimes, and replay calls it `reproduced`. P2-253's run
(`docs/runs/2026-10-19-desktop-replay.md`) has two: Git Extensions' `AppSettings::GetDictionaryDir()` and
`::GetGitExtensionsDirectory()`, which read `System.Windows.Forms.Application.ExecutablePath`. A `probe` of each returns
`<root>\legacy\GitCommands` on one side and the `dotnet` install folder on the other. `reproduced` claims that the user
can run the divergence, so a harness difference must never produce one (ADR 0035 decision 2). P2-254 is the same kind of
difference for the working directory; this one is the executable's path, which a shared working directory does not fix.
When done, the two drivers of one case see one executable path, or a member whose outcome reads it is not constructible.

## Spec references
ADR 0035 decision 2, ticket P2-254, `src/Equiv.Execute/ChildProcessHost.cs` (`Start`), `ReplayDriverFactory.Driver`,
`docs/runs/2026-10-19-desktop-replay.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A replay or `probe` whose member returns the directory of `Environment.ProcessPath` (or `Application.ExecutablePath`)
   gives equal outcomes on both sides, or is `not-constructible`, never a compared difference; an integration test shows
   it.
2. The two replays above are rerun (`full --execute` of Git Extensions, or `probe`), and each one's value is given.

## Notes
- Filed 2026-10-19 by P2-253 (its criterion 3 reran the three `AppSettings` replays with `Microsoft.WindowsDesktop.App`
  5.0 available: `ReadBoolRegKey` is now equal on both sides, and these two are still `reproduced`, for this reason).
  No confirmation in P2-237, P2-245 or P2-253 rests on a path that holds a driver's executable.
