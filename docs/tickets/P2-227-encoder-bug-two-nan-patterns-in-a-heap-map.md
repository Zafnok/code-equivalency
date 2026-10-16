# P2-227 Encoder bug: a floating-point heap map can hold two NaN patterns that a refined query tells apart
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-221

## Goal
P2-221 found that a refined query, solved after `fpa2bv`, applies an uninterpreted function to a floating-point
argument's bits, so two NaNs (one value, many bit patterns) can get two answers while the replay, which has one NaN,
gives one. `ModelDecoder.EnsureDiverges` then throws "Encoder bug". P2-221 gives every NaN argument of a pure function
and of a call one pattern (`SortMapper.OneNaN`). It does not reach a heap map: `TraceEncoder.Call` passes each map the
call reads whole, as an array, to the call's functions, and a map whose values (or keys) are `float` or `double` can
hold a NaN input on one side and a NaN an operation made on the other at the same key. The two arrays are then two
arguments to the solver and one to the replay.

Not yet observed: no pair and no property test has drawn it. When done, a refined query whose sides store the one NaN
in a floating-point field by different routes and then make a call does not throw.

## Spec references
`docs/tickets/done/P2-221-encoder-bug-nan-float-to-int-refinement.md` (Notes: where the solver and the replay part
ways), `src/Equiv.Verify.Z3/SortMapper.cs` (`OneNaN`), `src/Equiv.Verify.Z3/TraceEncoder.cs` (`Call`), ADR 0053.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test in `RefinementSoundnessTests` stores a NaN parameter into a `double` field on one side and the same NaN
   through `conv.f64.f32` and `conv.f32.f64` on the other, then makes one call; it is not an encoder bug (first shown
   to throw without the fix, or recorded in Notes that it does not).
2. No verdict on a pair without a NaN in a floating-point map moves (the existing refinement tests pass unchanged).

## Tests
The pair of criterion 1.

## Out of scope
Maps whose values are not floating point. Trace events: P2-221's call probe showed event comparison does not tell two
NaN patterns apart.

## Notes
- Filed 2026-10-16 by P2-221.
