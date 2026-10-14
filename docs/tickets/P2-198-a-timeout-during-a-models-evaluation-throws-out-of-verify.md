# P2-198 A timeout that interrupts a model's evaluation throws `canceled` out of `Verify` instead of giving Unknown(Timeout)
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-112

## Goal
The wall-clock timeout interrupts the context (`Context.Interrupt`). A `Check` that is interrupted
returns unknown, and the ladder reports Unknown(Timeout). The arithmetic refinement reads the model
of a `sat` check between checks (`ArithmeticAbstraction.Broken`, through `SolverModel.Eval`), and
`Model.Eval` that is interrupted throws `Microsoft.Z3.Z3Exception: canceled`. Nothing between it and
`Z3Backend.Verify` catches it, so the pair has an exception and no verdict.

Seen on CI, not on a corpus run: the `gates (windows-latest)` job of PR #494 (run 38513771795,
2026-10-14) took 1h08m on a loaded runner, and three tests of `Z3BackendTests` failed with that
exception and that stack (`LoopLadder.Queries` -> `ArithmeticRefinement.Spurious` ->
`ArithmeticAbstraction.Broken` -> `SolverModel.Eval` -> `Model.Eval`):
`AHardMultiplicationWithA50MillisecondTimeoutIsUnknownTimeout`,
`AnOpaqueBehindAHardConditionTimesOutInTheSecondQuery` and
`TheContextIsDisposedOnEveryPath(name: "return-value")`. The PR changes nothing under
`Equiv.Verify.*`, and the same tests pass on `main`'s runs of the same day: the timer fired while the
model was being read, where on a fast runner it fires inside `Check`.

When done, an interrupt that lands anywhere in a query's work, a model's evaluation included, ends in
Unknown(Timeout) with the detail the timeout gives today.

## Spec references
ADR 0049 (the timeout), `docs/tickets/done/P2-112-interrupt-that-throws-ends-the-process.md` (the
interrupt's own exception), `src/Equiv.Verify.Z3/SolverModel.cs`,
`src/Equiv.Verify.Z3/Refinement/ArithmeticAbstraction.cs`, `src/Equiv.Verify.Z3/LoopLadder.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A test interrupts the context while a model is being evaluated, with no race (P2-112's seam is
   one way), and shows the `canceled` exception escaping `Verify` before the fix.
2. After the fix the same test gets Unknown(Timeout). An exception that is not an interrupt still
   escapes as it does today.
3. The three tests above pass on `gates (windows-latest)`.

## Files
`src/Equiv.Verify.Z3/` (where the interrupt is caught), `tests/Equiv.Verify.Z3.Tests/`.

## Size guard
One pull request.

## Out of scope
- The interrupt's own exception on the timer thread (P2-112).
- Which budgets the timeout uses (ADR 0049).

## Notes
- Filed 2026-10-14 from PR #494 (P2-168), whose Windows gate failed on it.
