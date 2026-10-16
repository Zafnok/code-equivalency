# P2-224 Precision: the ICU rows fire on an ordinal comparison a solution getter returns
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-075 (done) exempts a call from the ICU rows (`ordinalUnaffected`) when it passes `StringComparison.Ordinal` or
`OrdinalIgnoreCase` as a constant. P2-212's adjudication (`docs/runs/2026-10-15-precision-verdict.md`) found a false
EQ006 that P2-216 left on its size guard, data flow beyond one method:
- `duplicati-3124`, `Duplicati.Library.Snapshots.UsnJournalService::IsPathEnumerated(string)`: the
  `System.String::Equals(string,System.StringComparison)` row on `Utility.ClientFilenameStringComparison`, a static
  get-only property of the solution whose body is `IsFSCaseSensitive ? StringComparison.Ordinal :
  StringComparison.OrdinalIgnoreCase`. Both branches are ordinal, so no culture data is read.

When done, a comparison argument that a solution getter or method returns, and whose every return is an ordinal
constant, exempts the call as a constant one does.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (`ordinalUnaffected`), `docs/tickets/done/P2-075-*.md`,
`docs/tickets/done/P2-200-*.md` (how a solution callee's returns are read, bounded and argued sound: the body that
runs, up to four calls), ADR 0019.

## Acceptance criteria (all must hold; nothing beyond them)
1. A `StringComparison` argument is ordinal when it is a constant, a conditional expression both of whose branches are
   ordinal, or a call to a solution member whose body is the one that runs (P2-200's rule) and returns at least once and
   only ordinal values by these rules, within P2-200's bound.
2. A sample with such a getter, Equivalent by congruence, and one whose getter can return `CurrentCulture`, EQ006.
3. `duplicati-3124` rerun in compare mode quick; Notes give `IsPathEnumerated`'s new result and every other result
   that moved.

## Tests
The ordinal-comparison tests; the sample.

## Size guard
A whole-solution call graph: stop.

## Out of scope
The single-method shapes (P2-216), the other cross-method exclusions (P2-220).

## Notes
- Filed 2026-10-15 by P2-216 (its size guard: the getter is another method's body).
