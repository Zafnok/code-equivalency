# P2-068 A Divergent that rests on a BCL call's answer the real member cannot give is not EQ002
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-060

## Goal
ADR 0041 stops a closed call (a `string` or primitive member handed only strings and primitives)
from writing the heap. Its result and `threw` flag are still free functions that the solver
chooses. P2-060 measured what that costs. With the differential gate's rule 2 exemption narrowed to
open calls, the 5,000-pair nightly budget fails (seed `9v8KtBVEhjFh`, DropNullCheck under IL) on a
model in which `Nullable<int>.GetValueOrDefault()` throws. That call throws on one side before a
statement the other side runs first. A result works the same way: nothing stops the model from
having `String.Concat` return null. No real member does either, so every such result on real code
is a false EQ002. Decide, through
`equiv-adr`, how a Divergent that rests on a closed call's answer is kept out of EQ002. Two
candidates are a no-throw and non-null summary per member, and tainting a closed call's outputs in
the replay, which would amend ADR 0026. Then implement that.

## Spec references
ADR 0041, ADR 0026 and its two 2026-09-30 clarifications, ADR 0018, VERIFICATION-MODEL section 7
(rule 2).

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change.
2. The gate pair at seed `9v8KtBVEhjFh` is pinned in `DifferentialSoundnessTests`. Its verdict is
   Unknown or Equivalent, or a Divergent that replays.
3. Rule 2's exemption covers only models whose run records an open call. Rules 1 to 3 hold on
   the PR and nightly budgets under both lowerings.

## Tests
- `DifferentialSoundnessTests.AClosedCallsAnswerIsOneTheMemberCanGive`
- the unit tests the decision names

## Out of scope
Open calls (they may still write the heap, ADR 0026's first clarification). Callee summaries for
anything but closed calls.

## Notes
