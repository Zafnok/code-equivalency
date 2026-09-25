# P2-019 An array's length is never negative in a model
Status: todo
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
