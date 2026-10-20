# P2-263 Precision bug: a `File.Delete` behind `File.Exists` of the same path is still an exposure in Duplicati's `Runner::Run`
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`File.Exists` returns false on both runtimes for a path holding a character .NET Framework rejects, so a call it guards
never receives one (P2-074's guard rule, P2-217's field and auto-property reads). P2-247
(`docs/runs/2026-10-20-precision-execute-verdict-2.md`, appendix C) found `Duplicati.Server.Runner::Run(IRunnerData,bool)`
an unwitnessed exposure whose `File.Delete` is guarded by `File.Exists` of the same path and whose string comparisons
are ordinal (`Utility.ClientFilenameStringComparison`, `OrdinalIgnoreCase`): a false positive. When done, the guard
shape in that body is followed, or the ticket's Notes say which form it is and why P2-074 and P2-217 do not read it.

## Spec references
`ValidPaths` (P2-074), tickets P2-217, P2-222, P2-224 (ordinal comparison a solution getter returns),
`docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test reproduces the body's guard shape and shows the path row does not fire behind it.
2. `duplicati-3124` in `full`: `Runner::Run(IRunnerData,bool)` is given with its result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247.
