# P2-271 Precision bug: an `as string` read of a call-context slot the solution writes only strings to is Divergent from the plain read
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`Duplicati.Library.Localization.LocalizationContext::.ctor(CultureInfo)` (`duplicati-3124`) is a false EQ002 in P2-212's
sample that P2-213's committed config did not clear (P2-213's Notes call it a real procedure-level difference). The
legacy side keeps `CallContext.LogicalGetData(key)` as it is; the modern side reads its own `CallContext.GetData(key) as
string`, so a non-string value in the slot becomes `null`. The only writers of that key are this class and
`LocalizationService`, and both write `CultureInfo.Name`, a string, or the value read back, so no execution of the
solution puts a non-string there. It is still EQ002 at `4b2e0df6` (P2-247,
`docs/runs/2026-10-20-precision-execute-verdict-2.md`). When done, the slot's value is typed by what the solution writes
to it (or an assumption names it), and the result is decided without a non-string value, or the Notes say why not.

## Spec references
`tools/corpus/configs/duplicati-3124.json` (P2-213), ticket P2-213 (its Notes, row 3), ADR 0035,
`docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test: a pair whose legacy side reads a slot raw and whose modern side reads it `as string`, with every write
   to the slot a string, is Equivalent (or Unknown with the assumption named), not Divergent.
2. `duplicati-3124` in `full`: `LocalizationContext::.ctor(CultureInfo)` is given with its result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247 (criterion 8): P2-213, the cause P2-212 named, is done and left this result.
