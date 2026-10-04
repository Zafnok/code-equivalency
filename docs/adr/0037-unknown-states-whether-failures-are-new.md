# ADR 0037: An Unknown says whether the modern side can fail where the legacy side does not

Status: accepted (2026-09-24); superseded in part by 0049 (a `timeout` pair is queried in thorough mode)

## Context
An Unknown pair claims nothing beyond its residual claim (ADR 0029). The regression migrations
most often introduce is a new failure: a null dereference, a new exception, or a stricter parse.
The fix they most often make is the reverse. Git Extensions' "no functional change" PR added
`?? string.Empty`, which removes a failure. Differential assertion checking (Lahiri, McMillan,
Sharma and Hawblitzel, FSE 2013, the SymDiff work) proves this weaker property in cases where full
equivalence is out of reach. Alive2 (PLDI 2021) uses the matching idea of refinement: the new
version may be more defined than the old one.

## Decision
When a matched pair ends Unknown for any reason other than `unbound` or `timeout`, the backend
runs two more queries over the same product program. Each one keeps only the throw observables
and drops the return value and the heap from the comparison:
- **no new failures:** every input on which the legacy side returns normally also returns
  normally on the modern side;
- **no removed failures:** the same with the sides swapped.

Execution past an `IrOpaque` is not modelled (ADR 0014). So on an input where a side reaches an
unshared opaque node, that side's outcome is unknown: it may return normally or fail.
`none-proved` must hold for every such resolution, and `found` needs a model on which neither
side reaches one, mirroring ADR 0014's two queries. An opaque node that occurs on both sides is a
shared call (ADR 0024), and its `threw` flag is shared like any other call's. `none-proved`
therefore never rests on an input the encoding did not model. A `timeout` pair is not queried:
the weaker query seldom finishes where the full one did not, and it would triple that pair's cost.

The results go in `properties.failureRefinement`. Each of `newFailures` and `removedFailures` is
`none-proved`, `found` (with a model) or `unknown`. The verdict stays EQ003. The rule id, the
exit code and the result fingerprint do not change. A `found` new failure is not EQ002: it may rest
on the same opaque node or abstraction that made the pair Unknown. It carries the ADR 0026 taint
check, and only an untainted one is reported with its model.

## Why
- The property is weaker, so fewer inputs reach an opaque node that matters. A throw is decided by
  guards, which are usually lowerable even when the value computations are not. This pays off
  where the guards run before the first unshared opaque node. An unshared opaque node ahead of
  the guard still makes the answer `unknown`.
- It answers the question reviewers ask first about an Unknown: can this now blow up where it
  did not before?
- Keeping the verdict EQ003 keeps every existing verdict rule and exit code intact.

## Rejected
- **A new rule id (EQ007) per property:** it would double the result count and invent a verdict
  the claim does not support.
- **Promoting a `found` new failure to EQ002 without the taint check:** it would reopen the
  false-Divergent path ADR 0026 closed.
- **Partition-based verdicts (PASDA, Glock et al., JSS 2024):** these are a finer version of the
  same idea. Their best-effort classifications for undecided cases are heuristics and would need
  their own ADR.

## Consequences
- VERIFICATION-MODEL.md section 6 gains `properties.failureRefinement` once this ADR is accepted.
- Unknown pairs other than `unbound` and `timeout` cost two more solver queries each. Each gets
  the pair's timeout, and the census reports the time spent on them.
- Ticket P1-013.

## Clarifications
- 2026-10-04 (P1-035). A `timeout` pair is queried. The Decision's sentence "A `timeout` pair is not
  queried: the weaker query seldom finishes where the full one did not, and it would triple that
  pair's cost" and the "and `timeout`" of the Consequences no longer hold. ADR 0049 superseded them,
  and P1-021 measured both reasons on the 164 `timeout` Unknowns of `gitextensions-8522`
  (`docs/runs/2026-10-04-failure-refinement.md`). Of the 138 still a `timeout` at that commit, 17
  (12.3%) get an answer from at least one query (19 of 140 in a second run): `newFailures` is
  `none-proved` on 2, `found` on 14, and the solver gives up on 69 (50%). The two queries add 0.44
  times the pair's time, not twice it. So the backend runs both queries on an Unknown of any reason;
  an `unbound` pair still never reaches it. Nothing else in the Decision changes: the verdict stays
  EQ003, `unknownReason` stays `timeout` whatever the queries answer, the rule id, the exit code and
  the fingerprint do not change, and a `found` carries the ADR 0026 taint check and is not EQ002.
  Which mode asks is ADR 0049's decision 2, implemented by P1-032; until that lands every run asks.
