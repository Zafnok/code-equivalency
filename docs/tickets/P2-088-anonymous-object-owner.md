# P2-088 Decide whether an anonymous object stays opaque
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`AnonymousObjectCreation` is an opaque node (P2-024 made it one, with a reason, instead of a crash).
On P2-065's run of `eshop-manual` it alone keeps 2 of 26 changed pairs opaque, 7.7%, which is over
ADR 0028's 5% line, and P2-024 is done, so it has no open owner. The sample is small. On the larger
pairs it is well under the line: `duplicati-3124` 3 of 936 alone (0.3%, in 25), `gitextensions-8522`
6 bodies in all.

This ticket is the owner the rule asks for, and its first job is to decide whether there is work to
do. `IOPERATION-COVERAGE.md` says a real lowering needs a record-like IR sort that does not exist
(P2-027 has the same gap for tuples).

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `AnonymousObjectCreation` and `Tuple`, P2-024, P2-027,
ADR 0034 (per-ticket unlock rule), `docs/runs/2026-10-01-full-eshop-manual/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. For the two `eshop-manual` pairs and the 25 `duplicati-3124` changed pairs that carry the reason
   (a `--lower-only` run each), record in `## Notes` where the object goes: passed to a call as an
   argument, returned, read back through its properties in the same method, or captured by a lambda.
2. If at least half are "passed to a call as an argument", lower that case: the creation is a closed
   call whose arguments are the property values in declaration order, identified by the property
   names, so two sides that build the same object from equal values agree. Snapshot test; the
   `IOPERATION-COVERAGE.md` row is updated. Decide the identity scheme with `equiv-decide`.
3. Otherwise change no code: write the split, and close the ticket as "stays opaque until a
   record-like sort exists", naming the ticket or ADR that would add one.

## Files
`src/Equiv.Frontend.CSharp/Lowering/` and its tests, only if criterion 2 applies;
`docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
Named in criterion 2, if it applies.

## Size guard
A new IR sort is out of reach of this ticket: stop and go through `equiv-adr`.

## Out of scope
Tuples (P2-027). Reads of an anonymous object's properties.

## Notes
- Found by P2-065 (`docs/runs/2026-10-01-migrations-verdict.md`).
