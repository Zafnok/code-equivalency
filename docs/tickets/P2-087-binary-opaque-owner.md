# P2-087 A lifted or otherwise unlowered binary operator no longer keeps a changed pair opaque
Status: in-progress
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
- Criterion 1, measured on `main` at `17e4cf66` with a `--lower-only` run of `openra-17989` (10,120 matched pairs, 345
  changed; the 2026-10-01 run had 418). `Binary` is in 56 changed pairs and alone in 35. Those pairs hold 368 `Binary`
  opaque nodes, both sides counted:

  | form | nodes | changed pairs holding it |
  |---|---|---|
  | enum operands (no pointer operand occurs) | 304 | 45 |
  | other: `object == object` 28, `object != object` 4 | 32 | 10 |
  | operand types that differ: `string + object` 22, `object + string` 2 | 24 | 8 |
  | a nullable struct compared with the `null` literal (P2-083's form) | 6 | 1 |
  | lifted comparison: a user-defined `==` on a nullable struct | 2 | 1 |
  | lifted arithmetic | 0 | 0 |

  The enum nodes by operator: `==` 238, `!=` 60, `&` 4, `>=` 2. Lifted operators, which the title and criterion 2 expect,
  are 2 nodes of 368.
- The size guard does not trip: enum operands are 83% of the nodes.
- Decision: the form lowered is `==` and `!=` on two operands of one enum type (298 of the 304 enum nodes), as `eq` and
  `ne` on the enum's sort. The IR already takes both on a sort (`IrBinaryOp`), an enum constant is already the sort
  element of its underlying value (`TypeMapper.Constant`), `default(E)` is the element of `0`, and the solver already
  asserts a sort's literals distinct (`SortMapper.Distinctness`), so nothing in `Equiv.Core` or `Equiv.Verify.Z3` changes
  and no nullable value sort is needed. `<`, `>=`, `&`, `|` and `-` on an enum read the underlying integer, which the
  sort does not hold; they stay opaque with reason `Binary` (6 nodes here).
- Deviation: criterion 3's property test is written for a lifted operator. No lifted operator was lowered, so the
  property (`EnumEqualityLoweringTests.LoweredEnumEqualityAgreesWithCSharp`) is that the lowered enum `==` and `!=`
  agree with C# on random operands, as a parameter and as a constant, defined by the enum or not.
- Criterion 4, the same run with the lowering: `changedReasonSets["Binary"]` falls from 35 to 12, by 23 pairs (6.7% of
  the 345 changed pairs; the criterion asks for 21). `Binary` is now in 20 changed pairs, down from 56; changed pairs
  without opaque rise from 172 to 195 (49.9% to 56.5%); bodies holding a `Binary` opaque are 279 a side, where the
  2026-10-01 run had 709. Matched, congruent and changed pairs are unchanged.
- What is left alone is 12 of 345 changed pairs (3.5%), under ADR 0028's 5%, so `Binary` needs no further owner on this
  pair. The largest forms left are reference equality on `object` and `string + object`.
- No summary under `docs/runs/` was written: both runs are censuses for this ticket's own criteria, so the README's
  scoreboard is untouched.
