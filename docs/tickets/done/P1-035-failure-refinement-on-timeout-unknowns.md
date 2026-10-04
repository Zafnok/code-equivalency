# P1-035 A `timeout` Unknown says whether the modern side can fail where the legacy side does not
Status: done (PR #399)
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
- Bar test (criterion 1): the first row of `equiv-adr`'s table fits. ADR 0049, accepted, already
  supersedes ADR 0037's "A `timeout` pair is not queried" and names this ticket as the one that asks.
  So the vehicle is a dated bullet under `## Clarifications` in ADR 0037, with P1-021's counts. No new
  ADR.
- Until P1-032 lands there is one mode, so every run asks on a `timeout` Unknown. P1-032 makes quick
  skip it.
- Decision: three tests outside the Files list change, because each asserted the old behaviour
  through the real backend. `Z3BackendTests` (`TheContextIsDisposedOnEveryPath`,
  `ResourceExhaustion_IsTimeoutUnknown`); `Equiv.Cli.Tests`'
  `UnboundAndTimeoutPairs_AreNotQueried`, now `UnboundPair_IsNotQueried_TimeoutAndOpaquePairsAre`;
  and the integration test `RepeatedRunsAreByteIdentical`, whose `timeout` pair now puts
  `loweringCensus.failureRefinement` in the file. Its `milliseconds` is wall-clock, so the test masks
  that one number and compares everything else. `FailureRefinementTests.TimeoutPair_IsNotQueried` is
  replaced by the three tests the ticket names.
- Decision: the new tests reach a `timeout` with `resourceLimit` 1,000 and a ten-minute `timeoutMs`,
  as P2-050's test does, not with a 50 ms wall clock, so their refinement answers repeat.
  `TimeoutUnknown_RefinementThatGivesUpIsUnknown` expects `newFailures` `unknown` and
  `removedFailures` `none-proved`: its legacy side has no throw, so only one query can give up.
- Decision: no `Release:` footer. The SARIF shape is unchanged; an existing property appears on more
  results.
- Decision: criterion 5 is recorded here and adds no file under `docs/runs/`, as the criterion
  says, so the README's scoreboard is not touched. One plain `full` run per build; the
  `--fail-on unknown` run would repeat the same queries.

### Criterion 5: `full` run of `gitextensions-8522` (2026-10-04)
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f.
- Two runs at `402f024`, default config (`resourceLimit` 5,000,000, `timeoutMs` 60,000, bound 3),
  win-x64, started 80 s apart and run side by side on one box. "After" is this branch. "Before" is
  the same commit with `main`'s `Z3Backend.cs`, so the two differ only in the line this ticket
  changes. Both exit 1, 13,541 matched pairs, 13,742 results.

| | before | after |
|---|---|---|
| Wall-clock | 10,104 s | 11,730 s |
| `verify` phase | 6,727 s | 8,384 s |
| `contracts` phase | 3,168 s | 3,122 s |
| `timeout` Unknowns | 145 | 143 |
| `loweringCensus.failureRefinement` pairs | 412 | 556 |
| `loweringCensus.failureRefinement` time | 501 s | 2,306 s |

- The census's failure-refinement time goes from 501 s to 2,306 s: 1,805 s more for 144 more pairs.
  The `verify` phase grows by 1,657 s (25%) and the run by 1,626 s (16%).
- All 143 `timeout` Unknowns of the after run carry `failureRefinement`, are `method` scoped and are
  EQ003. None of the 145 in the before run carries it. By query:

| Query | `timeout` Unknowns | none-proved | found | unknown |
|---|---|---|---|---|
| `newFailures` | 143 | 2 | 15 | 126 |
| `removedFailures` | 143 | 3 | 15 | 125 |

- 19 of the 143 (13.3%) get an answer from at least one query: 14 are `found` both ways, 2
  `none-proved` both ways, 1 `none-proved` for `removedFailures` only, 1 `found` for `newFailures`
  only and 1 for `removedFailures` only. P1-021's spike measured 17 of 138 and 19 of 140.
- Rule ids: 13,741 of the 13,742 results have the same rule id in both runs, and every `timeout`
  Unknown of either run is EQ003 in the other. One result differs, and it is not a `timeout` Unknown:
  `GitUI.Avatars.TemplateFormatter::Create`1(string,System.Func<string, global::System.Func<TInput, string>>)`
  is EQ003 (`abstraction`) before and EQ006 after. Rung 1 returned a different model in each run, one
  whose replay is tainted and one whose replay is not. The refinement queries run after the ladder
  has given its verdict and only on an Unknown, so they cannot have produced the EQ006. This is the
  run-to-run variation of P2-100 and ROADMAP's repeatability row.
- 13 more results keep their rule id and differ in the fingerprint, all in the ladder's own answer:
  8 Divergents carry a different model; 3 `timeout` Unknowns name the wall-clock limit in one run
  and the resource limit in the other; and 2 are `timeout` before and `opaque` or `unaligned-loop`
  after, where a rung 2 query the resource limit ended in one run finished in the other. Two runs
  sharing a box make the wall-clock backstop fire more often than a run alone would.
- Nothing new to file: the variation is P2-100's, and the 14 pairs `found` in both directions are
  the ones Out of scope leaves to P1-020 and P1-031.
