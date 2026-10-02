# P2-087 A lifted or otherwise unlowered binary operator no longer keeps a changed pair opaque
Status: todo
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-083

## Goal
A binary operator the frontend does not lower is an opaque node with reason `Binary`: "a lifted
operator, and anything else" (`IOPERATION-COVERAGE.md`). Alone, it keeps this share of changed pairs
opaque:
- `openra-17989`: 26 of 418 (6.2%), and it is in 59 (14.1%), P2-065's census;
- `duplicati-3124`: 37 of 936 (4.0%), in 116 (12.4%), P2-065's census;
- `gitextensions-8522`: 39 of 1,143 (3.4%), in 105, P2-046's run.

ADR 0028 wants an owner for a reason at or above 5% of a pair's changed pairs. P2-046 named ADR
0039's IL fallback. P1-018 has since measured the fallback and left it off by default, so the reason
has no open owner. Find which operator forms are behind the count and lower the largest.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` row `Binary`, ADR 0034 (per-ticket unlock rule), M4-002 (the
`IrPure` catalogue that floating-point and decimal operators use),
`docs/runs/2026-10-01-full-openra-17989/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `Binary` opaque nodes of `openra-17989`'s changed pairs (a
   `--lower-only` run) by form: lifted comparison, lifted arithmetic, pointer or enum operands,
   operand types that differ, other. Counts in `## Notes` (operator kinds and types only).
2. The largest form lowers to IR. A lifted operator on `T?` lowers to the operator on `T` guarded by
   the operands' has-value flags, with C#'s rules for a null operand (comparison is false, equality
   compares the flags, arithmetic is null). Decide the representation with `equiv-decide` and log it;
   if it needs a nullable value sort the IR does not have, go through `equiv-adr`'s bar test first.
3. A test per lowered form in `tests/Equiv.Frontend.CSharp.Tests`, a property test (CsCheck) that the
   lowered lifted operator agrees with C# on random operands including null, and the
   `IOPERATION-COVERAGE.md` row updated.
4. On a re-run, `changedReasonSets["Binary"]` on `openra-17989` falls by at least 5% of changed
   pairs (21 pairs), or `## Notes` records why not.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows no form above a third of the nodes, lower none: record the split and stop.

## Out of scope
The IL fallback. The lowering crash on a binary operand with no type (P2-083).

## Notes
- Found by P2-065 (`docs/runs/2026-10-01-migrations-verdict.md`).
