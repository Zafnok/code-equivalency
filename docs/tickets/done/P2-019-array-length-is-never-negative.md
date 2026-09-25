# P2-019 An array's length is never negative in a model
Status: done (PR #190)
Effort: S
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M0-012

## Goal
M0-012's differential soundness gate found it at the nightly budget (5,000 pairs, rule 2, seed
`000000000000`): a Divergent verdict whose model gives the array parameter `u` the length
`-2147483648`. No CLR array has a negative length. The frontend lowers `u[i]` as an unsigned
compare of `i` against `length.<Sort>` read at `u` (HeapLowerer), and nothing constrains that
input, so the solver can pick a negative length. The unsigned compare reads it as more than 2^31,
so every index is in bounds. That input cannot happen, and a divergence that needs it is a false
Divergent: a precision and decoding bug, not a soundness one. It only adds inputs, so it cannot
hide a real divergence. The gate skips the symptom `the model gives u the length -` under this
ticket. M0-012 criterion 7 says the skip list must be empty before M3-003 lands.

## Spec references
VERIFICATION-MODEL.md sections 2 (heap) and 7 (differential soundness); ADR 0018; ticket P1-006.

## Acceptance criteria (all must hold; nothing beyond them)
1. No model the backend returns gives any reference a negative `length.<Sort>` value. How is the
   implementer's call through `equiv-extend-ir`: for example, the encoder asserts
   `length[r] >= 0` at every read of a `length.*` input.
2. A pair whose only divergence needs a negative length is not Divergent.
3. The skip entry for `P2-019` is removed from `DifferentialSoundnessTests`, and the nightly
   budget passes rule 2 at seed `000000000000`.

## Files
The encoder or lowering file the fix lands in, its tests, `tests/Equiv.Tests.Integration/DifferentialSoundnessTests.cs`.

## Tests
`ANegativeArrayLengthIsNeverAModel` (Verify.Z3 tests), plus the gate at the nightly budget.

## Notes
- Decision: the assumption lives in `ProductEncoder`: each `IrMapRead` of a `length.*` input adds `(bvsge target #x00000000)`,
  not guarded by the block's `reach`. The CLR never reads a null reference's length, so no caller input is lost. It is
  quantifier-free, and every ladder rung encodes through `ProductEncoder.Encode`, so bounded, lockstep and k-induction all
  get it.
- Decision: criterion 1 says "any reference", and the solver leaves unread references free, so `ModelDecoder.Inputs`
  also gives 0 wherever a decoded `length.*` map is negative. Every length the replay reads is already non-negative in
  the model, so no replay changes (`ANegativeLengthAtAnUnreadReferenceDecodesAsZero`).
- Decision: the tests go in the existing `Z3BackendTests` (`ANegativeArrayLengthIsNeverAModel`, two pairs that diverge
  only on a negative length, including the gate's `u[int.MinValue]` bounds-check shape;
  `ADivergentModelGivesEveryReferenceANonNegativeLength`) and `ModelDecoderTests`. There is no new encoder snapshot:
  the `kinds/` snapshots read no `length.*` input and are unchanged.
- Gate: with the skip removed, `EQUIV_DIFFERENTIAL_BUDGET=nightly` at seed `000000000000` passes all three rules
  (3/3, 10m13s in a Linux cloud container; M0-012 recorded about 4 minutes locally).
- Toolchain: the cloud container had no .NET SDK, and `dot.net` is blocked by its proxy. Ubuntu's `dotnet-sdk-10.0`
  (10.0.112) worked with `global.json` pinned to it locally only, and `.z3-feed` filled from the GitHub release (hash
  matches). On that SDK, `dotnet format analyzers` reports 13 CA1515/CA2007 findings in `Equiv.Verify.Z3.Tests`,
  identical before and after this change.
- Deviation: this PR also fixes a test-generator bug from M3-025 (#187), at the user's request, because it turned
  this PR's required `stryker (Equiv.Verify.Z3)` leg red. `LineScopedResidualClaimHolds` mutates a procedure that is
  already a mutant, and `IrGen.Edits` gave new variables fixed suffixes (`.kept`, `.one`, `.changed`, `.dup`), so a
  second edit of the same variable defined the same name twice (IR003). The test's random seed hit this in about half of
  all runs under `CsCheck_Threads=1`. `IrGen.Fresh` now lengthens a suffix until the procedure does not use it yet.
