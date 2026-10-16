# P2-227 Encoder bug: a floating-point heap map can hold two NaN patterns that a refined query tells apart
Status: in-progress
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
- Criterion 1: the pair does not throw, and did not throw before any fix: `ACallGivenAHeapHoldingANaNIsGivenOneNaN` is
  Equivalent on `main` (`c74d3068`) with `TraceEncoder.Call` unchanged. So nothing in `src/` changes. The pair has the
  shape of P2-221's `ACallGivenANaNIsGivenOneNaN` (a guard that only a NaN passes, `conv.f64.f32` then `conv.f32.f64` on
  the new side), so its refined query is the one in which P2-221's call argument threw; here the NaN reaches the call only
  through `mapwrite` into `field.C.d` and the heap pair.
- Why it does not throw: Z3 does not tell two NaN patterns apart inside an array. A throwaway probe (not committed) asked
  `isNaN(x) and f(store(m, k, x)) != f(store(m, k, NaN))`, with `f` an uninterpreted function of an
  `(Array C Float64)`, after `fpa2bv, bit-blast, smt` and after `Z3Backend.Pipeline`'s refined tactic chain: unsat both
  ways, and unsat with the NaN `conv.f32.f64(conv.f64.f32(x))` makes in place of the NaN literal. The same with the NaN as
  the key of an `(Array Float64 bv8)`: unsat. Sanity: with `1.0` in place of the second NaN, both are sat. So `fpa2bv`
  rewrites an uninterpreted function's floating-point argument into its bits (P2-221), but an array's floating-point
  values and keys keep IEEE equality, under which every NaN is one value. The new test is the end-to-end guard if a Z3
  upgrade changes that.
- Criterion 2: no verdict can move, as nothing in `src/` changed; `RefinementSoundnessTests` and `RefinementTests` pass
  (95 tests).
- Decision: no `OneNaN` over a heap map. It would cost a lambda (or `map`) per map per call in every refined query to
  guard a case Z3 already gets right.
