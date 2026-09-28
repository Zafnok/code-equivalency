# P2-041 The model decoder fails on a map the model gives as `as-array`
Status: done (PR #249)
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-001

## Goal
The Windows `gates` job failed on PR #239 (run 36367910418), which touched only frontend test code:
`SharedFragmentTests.AFragmentPairIsNeverEquivalentWhenItsProgramsDiffer` with
`CsCheck_Seed=0fgMMTPvcRt7` (mutation `FlipComparison`) threw "Encoder bug: the model gives a map in
a shape the decoder does not read (store chain over a constant array expected): (_ as-array k!10)"
from `ModelDecoder.Values.DecodeMap`, reached through nested maps while the replay's `ModelOracle`
answered an `IrCall`. Z3 may give an array value, even evaluated with completion, as `as-array` over
an auxiliary function (`k!N`) whose interpretation is in the model; the decoder must read it.

## Spec references
M3-001 (model decoding and replay); `src/Equiv.Verify.Z3/ModelDecoder.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `ModelDecoder.Values` decodes an `as-array` map from the model's interpretation of its function:
   each entry a written key, the else value the default. Nested maps decode recursively.
2. An `as-array` the decoder has no model interpretation for (rung 4's ground facts, or a function
   the model does not interpret) still fails loudly as an encoder bug.
3. A unit test in `Equiv.Verify.Z3.Tests` exercises both; the seed above passes.

## Out of scope
Any other array shape (lambda); configuring Z3's model output.

## Notes
- Decision: read `as-array` from `Model.FuncInterp` of the array's function (entries written over the else value) rather than configuring Z3's model output; `Values` takes the model as an optional constructor argument, so rung 4's ground facts (no model) keep failing loudly.
- Decision: the .NET API has no `MkAsArray`; the unit test builds `(_ as-array f)` through `ParseSMTLIB2String` with `f` passed as a declaration. Constraining a function to return an `as-array` (`g(5) = as-array f`) makes Z3 answer unknown, so the nested case constrains `g`'s value as a store and decodes `as-array g`.
- The seed does not reproduce locally: `CsCheck_Seed=0fgMMTPvcRt7` passes on this box with and without the fix (the test pins `seed` in code and the failing sample needed 0 shrinks). Which array shape Z3 gives is timing-dependent (solver timeouts pick the rung and the model), so the unit test, not the seed, is the regression test.
