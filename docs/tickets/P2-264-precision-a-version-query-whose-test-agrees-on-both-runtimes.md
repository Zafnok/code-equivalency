# P2-264 Precision bug: the `Environment.Version` and `Environment.OSVersion` rows fire on a test that gives the same answer on both runtimes
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
.NET Framework reports `Environment.Version` 4.0 and caps `Environment.OSVersion` at 6.2 for an application without a
compatibility manifest; .NET 5 reports 5.0 and the real version. A body that only compares the version against a
threshold both sides fall on the same side of gives the same outcome. P2-247
(`docs/runs/2026-10-20-precision-execute-verdict-2.md`, appendix C) found two sampled exposures, both false positives:
- Duplicati `HyperVUtility::.ctor()`: its one use is `Major > 6 || (Major == 6 && Minor >= 2)`, true for the capped
  6.2 and for every later real version;
- Git Extensions `W02` of appendix C: `Environment.Version` sends the legacy side to a .NET 4 Full registry check that
  is true on every machine net461 runs on, and the modern side returns true directly (a `probe` agrees).
When done, a version read whose every use is a comparison with a constant that both runtimes' values satisfy alike is
no exposure, and a use that reaches the outcome otherwise still is.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (the `Environment` rows), `RowPreconditions`,
`docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Unit tests: `OSVersion.Version.Major > 6 || (Major == 6 && Minor >= 2)` fires no row; returning
   `OSVersion.Version.Minor` does.
2. `duplicati-3124` and `gitextensions-8522` in `full`: both results are given with their result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247. The Git Extensions one needs the registry key's value read as the host's, which may be
  out of reach; the Notes say so if it is.
