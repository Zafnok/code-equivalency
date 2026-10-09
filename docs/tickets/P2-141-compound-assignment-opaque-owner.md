# P2-141 The compound assignments left opaque are counted, and the largest form lowers
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A compound assignment the frontend does not lower is an opaque node with reason
`CompoundAssignment`: a lifted operator, one with a conversion in or out that is not the identity, a
C# 14 instance `operator +=`, and a target of a kind the lowering does not take
(`IOPERATION-COVERAGE.md`). No ticket owns the reason. P1-028's row
(`docs/runs/2026-10-07-opaque-tail.md`), over the three large runs' 2,246 changed pairs:

| Reason | gitextensions-8522: bodies (legacy / modern), in, alone | gitextensions-9860 | jellyfin-13023 | Sum: in | Sum: alone | Marginal unlock |
|---|---|---|---|---|---|---|
| `CompoundAssignment` | 58 / 58, 20, 9 | 59 / 59, 8, 3 | 91 / 91, 32, 2 | 60 | 14 (0.6%) | 35 (1.6%) |

The marginal unlock is the changed pairs that hold this reason and otherwise only reasons an open
ticket owns. The later censuses in the same report give 21 of 1,743 (1.2%): Jellyfin's 13 are 0
there, since most of its changed pairs are congruent again after P2-113. It is the smallest of the
six reasons P1-028 files, and `Binary+CompoundAssignment` is one of its sets, so P2-087's lifted
operators may cover part of it. Count the forms, and lower the largest.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `CompoundAssignment`, `Increment`, `Binary`,
`Conversion`; ADR 0034 (per-ticket unlock rule); `docs/tickets/done/P2-022-pure-compound-assignment.md`;
`docs/tickets/done/P2-087-binary-opaque-owner.md`; `docs/runs/2026-10-07-opaque-tail.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `CompoundAssignment` opaque nodes of `gitextensions-8522`'s and
   `jellyfin-13023`'s changed pairs (`--lower-only` runs) by form: a lifted operator, a conversion in
   or out that is not the identity by operand and target type (`byte += int`, `char += int`,
   `long += int`, an enum, a `string` target, `float` or `double` mixed with an integer), a
   user-defined operator the P2-022 rule leaves out, and by target kind where the target is the
   cause (array element, indexer, `ref` local, tuple element, other). For each form give the nodes,
   the changed pairs it is in, the changed pairs it alone keeps opaque, and whether P2-087's lowering
   of the same operator as a `Binary` would cover it. Counts in `## Notes` (operator kinds and types
   only).
2. The form with the most changed pairs alone lowers to IR as the existing compound assignment does:
   the target read once, the operand converted as C# converts it, the operator's exception edges,
   the result converted back, the write. Decide the representation with `equiv-decide` and log it.
3. A test per lowered form in `tests/Equiv.Frontend.CSharp.Tests`, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator, and the
   `IOPERATION-COVERAGE.md` row updated.
4. On a re-run, the changed pairs of `gitextensions-8522` that hold `CompoundAssignment` fall by at
   least 1% of changed pairs, or `## Notes` records why not.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows that the forms P2-087 would cover hold more than two thirds of the changed
pairs alone, lower nothing: record the split, add a line to P2-087's Notes and stop. More than two
forms lowered means the ticket was misread.

## Out of scope
Lifted binary operators themselves (P2-087). `??=`, which is another operation kind. The IL fallback.

## Notes
- Found by P1-028 (`docs/runs/2026-10-07-opaque-tail.md`): sixth by marginal unlock, and the last
  the cap of six admits; `iterator` (27 pairs, 1.2%) is the next.
