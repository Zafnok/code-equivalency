# P1-013 Failure refinement: every Unknown says whether the modern side can fail where the legacy side does not
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-016, M3-025; ADR 0037 accepted

## Goal
For every Unknown pair except `unbound` and `timeout`, run ADR 0037's two extra queries over the
same product program:
- **no new failures:** legacy returns normally ⇒ modern returns normally;
- **no removed failures:** the same with the sides swapped.

Record the result in `properties.failureRefinement`. The verdict stays EQ003. A reviewer can then
tell "Unknown, but it cannot start throwing" apart from "Unknown, and it can".

## Spec references
ADR 0037; ADR 0026 (taint on any model); ADR 0029 (scope); VERIFICATION-MODEL.md section 6.

## Acceptance criteria (all must hold; nothing beyond them)
1. `FailureRefinementQuery` in `Equiv.Verify.Z3` builds each query from the pair's existing
   product encoding. Only the throw observables are compared, and the return value and final heap
   are dropped. As in ADR 0014, a side that reaches an unshared `IrOpaque` has an unknown outcome
   (it may return or throw): first ask for a new failure on which neither side reaches one
   (`found` if sat); only if that is unsat, ask again letting an opaque-reaching side take either
   outcome (`none-proved` if unsat, else `unknown`). The same for `removedFailures`.
2. Each query's outcome is:
   - `none-proved` (unsat);
   - `found` (sat, and the model replays untainted in `IrInterpreter` per ADR 0026: it carries
     the model);
   - `unknown` (timeout, a tainted model, or a failure possible only through an opaque node).

   `properties.failureRefinement = { newFailures, removedFailures }`, each with its outcome and
   an optional `model`.
3. Each query gets the pair's `--timeout-ms`. `loweringCensus` (or the run properties, whichever
   already carries timing) reports the total time spent on these queries.
4. Verdict, rule id, exit code and result fingerprint are unchanged. A test asserts this over
   every sample.
5. New sample `samples/unknown-new-throw`. The legacy side lowers fully and always returns. The
   modern side adds a lowered guard that throws, and after the guard an unshared opaque value
   computation, which makes the pair Unknown. Expected:
   `newFailures: found`, `removedFailures: none-proved`. Its README states this, and the snapshot
   is checked in.
6. VERIFICATION-MODEL.md section 6 documents `failureRefinement`, citing ADR 0037.

## Files
`src/Equiv.Verify.Z3/FailureRefinementQuery.cs`, the backend's Unknown path,
`src/Equiv.Core/Reporting/SarifReportWriter.cs`, `samples/unknown-new-throw/**`,
`docs/VERIFICATION-MODEL.md`, tests, snapshots.

## Tests
`NewFailure_Found_WhenGuardRemoved`, `NoNewFailure_Proved_WhenOnlyValuesDiffer`,
`TaintedFailureModel_IsUnknown`, `ModernReachesOpaque_IsNotNoneProved`,
`Refinement_NeverChangesVerdictOrFingerprint`, `UnboundAndTimeoutPairs_AreNotQueried`.

## Size guard
New rule ids, or exit code changes, are ADR 0037's rejected alternatives. Stop.

## Out of scope
Partition verdicts. Exception-message comparison (section 1 does not observe messages).

## Notes
