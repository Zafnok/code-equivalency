# M4-016 `EtaEstimator.Estimate` overflows to a negative ETA for a tiny done weight on a long phase
Status: in-progress
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-012

## Goal
`IsNonNegativeAboveTheItemThreshold` failed once in a full `Equiv.Core.Tests` run on 2026-09-27
and then passed on reruns. At 2,000,000 iterations it fails with seed `571VKAzBWW29`, which shrinks
to done 9, extra 785,039, items 8,559, ticks 154,402,185,409. That is 4.3 hours elapsed with 9 of
785,039,009 weight done. `elapsed.Ticks * remaining / doneWeight` is about 1.35e19. That is more
than `long.MaxValue`, so the unchecked `(long)` cast of the `Int128` wraps and the ETA comes out
negative. The inputs are inside what ADR 0038 specifies (any elapsed time, any weights, the item
threshold met), so the fix belongs in the estimator, not the generator.

## Spec references
ADR 0038; M4-012 criterion 6.

## Acceptance criteria (all must hold; nothing beyond them)
1. `Estimate` saturates at `TimeSpan.MaxValue` rather than wrapping when the quotient does not fit
   in a `long`. A `ceiling` still clamps it.
2. The property tests keep their generators and iteration counts.

## Files
- `src/Equiv.Core/Progress/EtaEstimator.cs`

## Tests
- `EtaEstimatorTests.SaturatesInsteadOfWrappingWhenTheEstimateOverflows` (the shrunk counterexample)

## Size guard
More than 1 non-test file changed: stop and re-read.

## Out of scope
The threshold arithmetic (`doneWeight * 100`), which only overflows for weights above 9e16.

## Notes
Decision: saturate with `Int128.Min(..., long.MaxValue)` before the cast rather than a checked cast or a branch; the ceiling then applies as before, and no new branch needs covering.
