# P2-217 Precision: a path row fires behind a `File.Exists` guard on a field or property read
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-074 (done) stops a path-validation row from firing on a path a validating guard checked: `File.Exists(p)`
returns false for any path .NET Framework rejects, so a call under `if (File.Exists(p))` never sees one. It reads the
guard on a parameter or a local nothing reassigns. P2-212's adjudication (`docs/runs/2026-10-15-precision-verdict.md`)
found 3 false EQ006 in its fresh sample of `duplicati-3124` where the guarded value is a member read twice, at the
guard and at the call, with nothing between:
- `Duplicati.UnitTest.BorderTests::TearDown()`: `if (File.Exists(this.f)) File.Delete(this.f);` on a `readonly` field;
- `Duplicati.Library.Utility.TempFile::Dispose(bool)`: `File.Exists(m_path) && ...` then `File.Delete(m_path)` on a
  private field;
- `Duplicati.Server.SingleInstance::m_filewatcher_Created(...)`: an early return unless `File.Exists(e.FullPath)`, a
  property of a parameter, then `File.Delete(e.FullPath)`.

And 1 in `gitextensions-8522`'s sample whose legacy throw a catch-all swallows where the modern side reaches only
`File.Exists`, which returns false for the same path: `GitCommands.PathUtil::TryFindShellPath(string,out string)`,
`try { var p = Path.Combine(root, "x", arg); if (File.Exists(p)) return true; } catch { } return false;`, so both
sides return false for an invalid `arg`. The solver leaves `File.Exists` unconstrained.

When done, a guard on a member read that nothing between the guard and the call can write is followed, and the
largest remaining shape is decided.

## Spec references
`docs/tickets/done/P2-074-*.md` (the guard rule and its soundness), `docs/tickets/done/P2-201-*.md` (which member
reads are stable), ADR 0040.

## Acceptance criteria (all must hold; nothing beyond them)
1. A `File.Exists` (or other listed guard) on a field of `this`, a `readonly` field, or a property of a parameter
   whose getter is an auto-property, guards a later read of the same member when no write to it, no call and no
   `await` lies between: Notes give the soundness argument, and why a call between breaks it.
2. `equiv-decide` decides whether the swallowed-throw shape (`TryFindShellPath`) is taken here: `File.Exists`
   returning false for every path the row's precondition names is a fact of both runtimes.
3. Sample pairs, and `duplicati-3124` and `gitextensions-8522` rerun in compare mode quick; Notes give the four
   procedures' new results.

## Tests
The path-row tests; a sample where a call between guard and use writes the field and the row still fires.

## Out of scope
A mutable property with a body. Paths from callers (P2-207).

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6).
