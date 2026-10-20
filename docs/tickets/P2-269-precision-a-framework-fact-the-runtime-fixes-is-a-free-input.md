# P2-269 Precision bug: a framework fact the pair's own runtime fixes is a free input per side, so three edits are solver Divergent on inputs no host gives
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-245 (`docs/runs/2026-10-19-execution-reach.md`) refuted three EQ002 of P2-212's sample by hand trace with a `probe`
beside it, and filed no ticket for their cause; P2-247 (`docs/runs/2026-10-20-precision-execute-verdict-2.md`) found all
three still Divergent at `4b2e0df6`. In each, the model gives a framework query an answer the side's own runtime never
gives:
- Git Extensions `CommonTestUtils.MockExecutable.MockProcess::Verify()`: the added `CanRead` guard decides only for a
  closed stream still held by `StandardInput`; the solution closes input only through `StreamWriter.Close`, which on
  .NET Framework nulls `BaseStream` and on .NET 5 leaves `CanRead` false, so both sides skip;
- Git Extensions `GitUI.Theming.ThemeModule::ResetGdiCaches()`: the model has .NET Framework's reflection lookups of
  `System.Drawing` members fail (an `InvalidCastException`); on 4.8 the members exist with the expected types;
- Duplicati `Duplicati.GUI.TrayIcon.Program::TryGetWinforms()`: the bodies differ only when
  `Platform.IsClientWindows` is false, a non-Windows host, where .NET Framework 4.8 does not run.
When done, each of the three is decided without the impossible answer (a fact or a declared assumption that the result
names, ADR 0035), or the Notes record why one cannot be.

## Spec references
ADR 0035, ADR 0040 decision 3 (the detected runtime), ticket P2-250 (a framework `static readonly` value both sides read),
`docs/runs/2026-10-19-execution-reach.md` (rows 19, 20 and Duplicati 22), `docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. For each shape, a unit test shows the side's runtime fact is used (or the assumption named) and the solver no longer
   reports a divergence that rests on the impossible answer.
2. `gitextensions-8522` and `duplicati-3124` in `full`: the three are given with their result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247 (criterion 8: every false-positive cause names an owning ticket). Three shapes, one cause:
  a size guard may split them.
