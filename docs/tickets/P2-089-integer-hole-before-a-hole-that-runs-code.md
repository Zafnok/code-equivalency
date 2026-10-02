# P2-089 Decide whether an integer hole followed by a hole that runs code stays opaque
Status: todo
Effort: S
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-086

## Goal
P2-086 lowers an interpolated string as the concatenation of its parts when that is what both bindings
compute. It leaves one shape opaque on purpose: an integer hole followed by a hole that calls
something, such as `$"{a.Count()} of {b.Count()}"`. `string.Format` (.NET Framework) formats the first
integer after `b.Count()` has run, and `DefaultInterpolatedStringHandler` (.NET 6 and later) before it
runs. The two agree unless that later hole changes the current culture, and nothing in the model says
it does not.

After P2-086, `InterpolatedString` alone still keeps 2 of `eshop-manual`'s 26 changed pairs opaque
(7.7%), over ADR 0028's 5% line, and 2 is the number of pairs P2-086's split found with this shape. On
`duplicati-3124` the reason alone is 2 of 936 (0.2%) and on `gitextensions-8522` 7 of 1,294 (0.5%).
This ticket is the owner the rule asks for. Its job is to decide whether the model may assume that
evaluating a hole leaves the current culture as it found it.

## Spec references
P2-086's Notes (the split and its `Decision:` lines), VERIFICATION-MODEL.md section 1 (what we claim)
and section 3 (the interpolated string rule), ADR 0041 (closed calls and ambient state), ADR 0018
(a call's result depends on its position), `docs/tickets/IOPERATION-COVERAGE.md` row
`InterpolatedString`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Run `equiv-adr`'s bar test on the assumption "evaluating a hole of an interpolated string does not
   change the current culture". It is a new line in VERIFICATION-MODEL section 1, so expect a new ADR:
   write it as proposed, in its own PR, and stop there until it is accepted.
2. If the ADR is accepted: `InterpolatedStrings.IsConcatenation` drops the condition on the holes
   after the first integer hole, the two bindings still lower to the same IR (snapshot test with both
   bindings), and the `IOPERATION-COVERAGE.md` row and VERIFICATION-MODEL sections 1 and 3 say so.
3. If it is rejected: change no code, and record in `## Notes` that the shape stays opaque and why.
4. Either way, `changedReasonSets["InterpolatedString"]` on `eshop-manual` after the decision goes in
   `## Notes`.

## Files
`docs/adr/` (the proposed ADR); then `src/Equiv.Frontend.CSharp/Lowering/InterpolatedStrings.cs` and its
tests, `docs/VERIFICATION-MODEL.md`, `docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
Named in criterion 2, if it applies.

## Size guard
Other hole types (`bool`, `char`, enums, `Nullable<T>`, floating point, `object`) and format or
alignment clauses are not this ticket: no measured pair needs them.

## Out of scope
The IL fallback. Changing how a closed call's result depends on its position (ADR 0018, ADR 0041).

## Notes
- Found by P2-086's re-run of the `eshop-manual` census.
