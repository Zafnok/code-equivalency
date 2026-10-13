# P2-170 A deconstruction of a tuple-typed value lowers as its stores, not as an opaque
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-025

## Goal
P2-025 lowers `(a, b) = (x, y)`, a deconstruction of a tuple literal. `IOPERATION-COVERAGE.md`'s
`DeconstructionAssignment` row lists what it left opaque: a tuple-typed value, a `Deconstruct`
method, a nested tuple, a property or array element target, and a deconstruction whose value is
used.

On `gitextensions-11284` (IDE0008, explicit types for `var`) that remainder is the largest single
reason a changed pair is not decided. In P2-110's quick run at equiv `78853202`
(`docs/runs/2026-10-13-cleanup-gitextensions-11284/SUMMARY.md`):
- `DeconstructionAssignment` is in the reason set of 46 of the 178 changed pairs (25.8%), and the
  whole reason set of 28;
- it is the first reason of 34 Unknowns, the largest group of the review list;
- the pull request rewrites 38 deconstructions from `var (a, b) = ...` to explicit types. By a text
  scan of the diff, 26 of the 38 take apart the result of a call, 9 an array element, 2 an awaited
  value and 1 a new object. None takes apart a tuple literal. So the form to lower first is a tuple-typed value.

In the thorough run of the same pair the IL pass proves 22 pairs that are `opaque` in the quick
run, and the review-list group falls from 34 to 18. So a good part of these pairs is provable once
the construct lowers. Quick mode, the default, proves none of them.

When done, a deconstruction of a tuple-typed value into locals lowers as one evaluation of the
value and one store per element, in the order the language gives, and the two spellings (`var (a,
b)` and `(T a, U b)`) lower to the same IR.

## Spec references
`IOPERATION-COVERAGE.md` rows `DeconstructionAssignment` and `Tuple`; VERIFICATION-MODEL section 3;
`docs/tickets/done/P2-025-deconstruction-assignment.md` and P2-027 (the `IrTuple` sort and its
limits); `.claude/skills/equiv-extend-ir`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count first: over both sides of `gitextensions-11284`, how many opaque deconstructions each form
   in the coverage row accounts for (tuple-typed value by where the value comes from and by element
   types; `Deconstruct` method; nested; non-local target; value used). Record the table in Notes.
2. The largest form lowers, and `IOPERATION-COVERAGE.md`'s row says so. If the largest form needs a
   tuple sort P2-027 does not have (an element that is not `bool` or integral), say in Notes what
   was done about it through `equiv-decide`.
3. A sample pair that changes only `var (a, b) = F()` to `(T a, U b) = F()` is Equivalent; a sample
   that swaps the two targets is Divergent.
4. Whatever stays opaque still writes its local and parameter targets with unknown values (P2-009).
5. Rerun `gitextensions-11284` in compare mode quick. Notes record the reason-set count next to 46
   and the solver-proved count next to 73 of 178.

## Tests
- `IrLowererTests` and `IrLowererSnapshotTests`: one per new shape, and one for a shape that stays
  opaque.
- `DeconstructionEquivalenceTests`: criterion 3's two samples.
- `LoweringOracleTests` covers the new shape.

## Size guard
More than one form of the coverage row lowered: stop, the next form is its own ticket.

## Out of scope
A `Deconstruct` method unless criterion 1 finds it the largest form; the IL pass.

## Notes
- Filed 2026-10-13 from P2-110's rerun of the cleanup pairs. No open ticket owned the reason.
