# P1-032 A `timeout` Unknown says whether the modern side can fail where the legacy side does not
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-013, P1-021

## Goal
ADR 0037 does not ask its two queries on a `timeout` Unknown, on the argument that "the weaker query
seldom finishes where the full one did not, and it would triple that pair's cost". P1-021 measured
both halves on the 164 `timeout` Unknowns of `gitextensions-8522`
(`docs/runs/2026-10-04-failure-refinement.md`). Of the 138 that are still a `timeout` at that
commit, 17 (12.3%) get an answer from at least one query (19 of 140 in a second run), and the two
queries add 0.44 times the pair's time, not twice it. A `timeout` Unknown is `method` scoped and
claims nothing today, so each answer is a claim the result did not have.

When done, a `timeout` Unknown carries `properties.failureRefinement` like every other Unknown the
backend sees.

## Spec references
ADR 0037, ADR 0026 (the taint check on a `found`), VERIFICATION-MODEL.md section 6
(`failureRefinement`), `docs/runs/2026-10-04-failure-refinement.md`,
`src/Equiv.Verify.Z3/Z3Backend.cs` (`Verify`), `FailureRefinementQuery.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `equiv-adr`'s bar test is run first and its outcome is in Notes. The expected vehicle is a dated
   clarification of ADR 0037: a `timeout` pair is queried, with P1-021's counts in place of the
   sentence that says it is not.
2. `Z3Backend.Verify` runs `FailureRefinementQuery` on an Unknown of any reason. `unbound` is
   unchanged: such a pair never reaches the backend.
3. The verdict, the rule id, the exit code and the fingerprint of a `timeout` Unknown do not change.
   Its `unknownReason` stays `timeout` whatever the two queries answer.
4. VERIFICATION-MODEL.md section 6 says "every Unknown other than `unbound`".
5. On a `full` run of `gitextensions-8522` through `equiv-corpus-run`, Notes records the
   `failureRefinement` outcomes of the `timeout` Unknowns by query, the census's
   `loweringCensus.failureRefinement` time before and after, and that no result's rule id changed.

## Files
`src/Equiv.Verify.Z3/Z3Backend.cs`, `src/Equiv.Core/Verdicts/Unknown.cs` (comment only),
`tests/Equiv.Verify.Z3.Tests/FailureRefinementTests.cs`, `docs/VERIFICATION-MODEL.md`,
`docs/adr/0037-unknown-states-whether-failures-are-new.md` (clarification only).

## Tests
`TimeoutUnknown_CarriesFailureRefinement`, `TimeoutUnknown_KeepsItsReasonAndFingerprint`,
`TimeoutUnknown_RefinementThatGivesUpIsUnknown`.

## Size guard
A change to what `found` or `none-proved` means, to the two queries themselves, or to any verdict:
stop.

## Out of scope
Promoting a `found` on a `timeout` Unknown to Divergent. P1-021 saw 14 such pairs, `found` in both
directions, which suggests the full query times out on pairs a narrower query decides; that is a
question for P1-020 and P1-031.
Skipping these queries in a quick run mode: that belongs to the run-modes ADR, if it lands.

## Notes
- Filed by P1-021. Its criterion 4 files this ticket at 5% of the `timeout` Unknowns with a
  `none-proved` `newFailures`, and the measured share is 1.4% (2 of 138). It is filed regardless, on
  the user's direction of 2026-10-04: measured yield orders work and does not by itself leave a
  sound gain unbuilt. P1-021's Notes record the deviation.
