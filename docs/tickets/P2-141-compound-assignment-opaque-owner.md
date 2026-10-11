# P2-141 The compound assignments left opaque are counted, and the largest form lowers
Status: in-progress
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
- Criterion 1, measured on `main` at `1f9bb699` with `--lower-only` runs whose `CompoundAssignment` reason carried the
  operator kind, the target's and the operand's type, the lifted flag, whether each conversion is the identity, the
  operator method and the target's kind (instrumentation not committed). `gitextensions-8522`: 13,592 matched pairs, 936
  changed, the reason in 18 and alone in 11. `jellyfin-13023`: 14,398 matched, 122 changed, in 7, alone in 3. Nodes are
  counted on both sides; "alone" is the changed pairs whose only opaque nodes are of that form:

  | form | gitextensions-8522: nodes, in, alone | jellyfin-13023: nodes, in, alone | would P2-087's `Binary` lowering cover it |
  |---|---|---|---|
  | `string += string`, no operator method, both conversions the identity | 50, 13, 8 | 32, 5, 3 | no; `Binary` lowers `string + string` as `Concat` since M2-004, and the compound form never reached it |
  | `enum \|= enum` | 40, 2, 1 | 4, 2, 0 | no; P2-087 lowered `==` and `!=` on an enum and left `\|` and `&` opaque |
  | `enum &= enum` | 14, 2, 0 | 0, 0, 0 | no, as above |
  | `string += object` | 2, 1, 1 | 0, 0, 0 | no; `string + object` is opaque as a `Binary` too |
  | `bool \|= bool` | 2, 1, 0 | 0, 0, 0 | no; `Binary` lowers `bool \| bool`, and P2-087 did not add it |
  | a lifted operator | 0 | 0 | |
  | a conversion in or out that is not the identity (`byte += int`, `char += int`, `long += int`, an enum, `float` or `double` with an integer) | 0 | 0 | |
  | a user-defined operator the P2-022 rule leaves out, a C# 14 instance operator | 0 | 0 | |
  | the target the cause (array element, indexer, `ref` local, tuple element, other) | 0 | 0 | |

  One changed pair of `gitextensions-8522` holds both enum forms and nothing else, which is the eleventh alone. The
  targets of `string += string` are locals, parameters, properties and one flow capture; no target kind is a cause, and a
  compound assignment whose target is not taken has the target's reason, not this one (none in a changed pair of either
  run). Every form the Goal names is absent: an integral target with a narrower or wider operand already lowers.
- The size guard does not trip: P2-087's lowering covers none of the forms.
- Decision: the form lowered is `+=` on a `string` target with a `string` operand, as the closed
  `System.String::Concat(string,string)` call `Binary` makes for `+` (ADR 0041), inside the existing read, operate, write
  of a compound assignment. So `s += t` and `s = s + t` lower to the same calls on every target the lowering takes, and
  nothing in `Equiv.Core` or `Equiv.Verify.Z3` changes. `string += object` stays opaque, as `string + object` is.
- Found by the oracle while adding the form: a compound assignment to a reference-typed local or parameter stored the
  value and left the variable's null shadow as it was, so `s += t; return s == null;` read the nullness of the old `s`.
  The write now stores the shadow from the `null.<Sort>` map at the value written, as a simple assignment of a call's
  result does. A static user-defined operator on a class (`c += d`, `c++`; P2-022) went through the same write and was
  wrong the same way on `main`: for those two forms this is a soundness fix, with
  `IrLowererTests.AReferenceACompoundAssignmentWritesIsNullWhenTheValueWrittenIs`.
- Criterion 4, the same `--lower-only` run of `gitextensions-8522` with the lowering: the changed pairs that hold
  `CompoundAssignment` fall from 18 to 5, by 13 (1.4% of the 936 changed pairs; the criterion asks for 1%). Alone falls
  from 11 to 3; bodies holding the reason from 62 to 26 a side; changed pairs without opaque rise from 488 to 494. Two of
  the eight pairs the form alone kept opaque now show another reason. Matched, congruent and changed pairs are unchanged.
  `jellyfin-13023` was not re-run: what is left there is `enum |= enum`, in 2 changed pairs.
- What is left is under 1% of changed pairs on `gitextensions-8522`, and 2 pairs on `jellyfin-13023`.
- Both runs of `gitextensions-8522` exit 5: two matched pairs fail to lower with an `ArgumentNullException` for a
  parameter named `method`, with and without this ticket's change. Filed as P2-158.
- The corpus was read from another worktree's `.corpus/` (the commits `tools/corpus/pairs.csv` pins); the runs' files are
  outside the repository. No summary under `docs/runs/` was written: the runs are censuses for this ticket's own
  criteria, so the README's scoreboard is untouched.
