# P2-156 Rung 1's arithmetic abstraction leaves `c / x` and `c % x` exact, and a procedure runs out of budget against itself
Status: in-progress
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-031

## Goal
`SoundnessPropertyTests.WithArithmeticAbstracted_AProcedureIsEquivalentToItself` (P1-031 criterion
5) failed on seed `4ubFDL-wLcDa`: `Verify(p, p)` with `ArithmeticMode.Forced` returned
`Unknown(timeout)`, "arithmetic abstracted, round 1: solver returned unknown (canceled): resource
limit 2000000 hit", no fact added. PR #457 does not touch this path.

The procedure holds `%t4 = urem %c.0, %a` and `%t35 = srem %t34, %v0.26`, both with a constant
dividend (`5`, `2147483648`) and an unknown divisor. P1-031 left any operator with a constant
operand exact (ADR 0025, clarification of 2026-10-07). For a product, or a division by a constant,
that is cheap for the solver. For a constant dividend over an unknown divisor it is not: the solver
still bit-blasts the whole 32-bit divider, and two of them exhaust the budget even when the product
of the procedure with itself is trivially unsatisfiable.

When this is done a `sdiv`, `srem`, `udiv` or `urem` whose divisor is not a constant is a shared
function, whatever its dividend; a `mul`, an overflow test and a division by a constant are as they
were.

## Spec references
`docs/tickets/done/P1-031-abstract-hard-arithmetic-on-a-timeout.md`, ADR 0025 (clarification of
2026-10-07), `docs/VERIFICATION-MODEL.md` (the second rung 1 encoding),
`src/Equiv.Verify.Z3/Refinement/ArithmeticAbstraction.cs`, `FragmentEncoder.cs` (`EncodeBinary`).

## Acceptance criteria
1. `Verify(p, p)` with the abstraction forced is Equivalent for seed `4ubFDL-wLcDa`, and the
   harness is stable over repeated random seeds.
2. A division or remainder with a constant dividend and an unknown divisor is a shared function in
   `EncodeAbstracted`, and `AppliesTo` says so; a constant divisor, and a product with a constant on
   either side, stay exact.
3. ADR 0025 has a dated clarification and VERIFICATION-MODEL.md describes the rule.

## Files
`src/Equiv.Verify.Z3/Refinement/ArithmeticAbstraction.cs`, `src/Equiv.Verify.Z3/FragmentEncoder.cs`,
`tests/Equiv.Verify.Z3.Tests/ArithmeticRefinementTests.cs`, `docs/adr/0025-*.md`,
`docs/VERIFICATION-MODEL.md`, `docs/ROADMAP.md`.

## Tests
`ConstantDividend_IsAbstracted`; `ConstantOperand_IsNeverAbstracted` now pins a constant divisor
and a constant multiplier; the `Operations` theory gains the constant-dividend and constant-divisor
rows; the harness test of criterion 1.

## Out of scope
Abstracting `shl`/`shr` by an unknown amount, or any other operator. Raising the harness's
budget.

## Notes
- Cause found by reproducing the seed with a throwaway test that printed the Unknown's ladder: the
  rung's only step was the round-1 timeout with `factsAdded` 0, so nothing was spurious; the exact
  dividers in the abstracted product were the cost. With the rule changed, the seed passes and six
  further random-seed runs of both `WithArithmeticAbstracted_*` harness tests pass.
- Bar test (`equiv-adr`): a dated clarification of ADR 0025's clarification of 2026-10-07, no new
  ADR. It narrows an exemption, adds no operator and changes no verdict's meaning; abstraction is
  sound for any operand pair, the exemption was only ever a precision and cost choice.
- Decision: a division is exempt only when its divisor is constant (not its dividend); a product
  stays exempt when either operand is, since it is commutative and cheap either way.
- Not run locally: `./build.ps1`. Targeted: all 742 tests of `Equiv.Verify.Z3.Tests` pass.
