# P1-013 Failure refinement: every Unknown says whether the modern side can fail where the legacy side does not
Status: in-progress
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
- Deviation: criterion 5's sample as written (guard *before* an unshared opaque value, legacy fully
  lowered and always returning) is Divergent, not Unknown. An input the guard rejects reaches no
  opaque node on either side, so ADR 0014's first query finds the divergence and rung 1 refutes the
  pair (EQ002). More generally, `found` needs an untainted model reaching no unshared opaque node, and
  that model also satisfies rung 1's first query, so on a backend Unknown `found` occurs only where
  rung 1's own model of that divergence replayed tainted (ADR 0026). `samples/unknown-new-throw` keeps
  the ticket's shape but computes the opaque value before the guard, so the pair is Unknown(opaque,
  line) with `newFailures: unknown` and `removedFailures: none-proved`, and its README says why.
  `found` is covered on IR by `NewFailure_Found_WhenGuardRemoved`. This is a spec-ambiguity note on
  ADR 0037's "Why" ("pays off where the guards run before the first unshared opaque node"), not a
  change to the decision. What that case yields in practice is `none-proved`, not `found`.
- Decision: the result types are `FailureRefinement(NewFailures, RemovedFailures)` in
  `Equiv.Core.Verdicts`, on `Unknown.FailureRefinement`, with each query a
  `RefinementResult(RefinementOutcome, Counterexample? Model)`. `Elapsed` sits on `FailureRefinement`
  and is left out of equality, because it is a measurement, not a result.
- Decision: the queries run on rung 1's product (shared fragments as calls, unrolled `k`), built
  afresh in `FailureRefinementQuery`. Past the bound of a looping pair counts as an unshared opaque
  node does (unknown outcome). An acyclic pair's `IrUnreachable` stays an assumption, as rung 1 treats
  it. A pair whose rung 1 was not applicable (irreducible control flow, an uninlinable self-call) is
  `unknown` on both queries without asking the solver.
- Decision: `found` is replayed with `ModelDecoder.Runs` and requires `Taint.Outcome` false on both
  sides, the same test `ModelDecoder.Compare` applies to an outcome difference.
- Decision: criterion 3's timing lives in `loweringCensus.failureRefinement: { pairs, milliseconds }`,
  since neither the census nor the run properties carried timing before. It is written only when
  some pair was queried. `SarifNormalizer` sets `milliseconds` to 0 for snapshots. The parity job
  compares only results, so it is unaffected.
- Decision: `UnboundAndTimeoutPairs_AreNotQueried` is in `Equiv.Cli.Tests` (FakeFrontend + real
  `Z3Backend`), because an unbound pair is decided in the CLI and never reaches the backend.
  `Refinement_NeverChangesVerdictOrFingerprint` is in `SamplesEndToEndTests`: a theory over every
  sample, against a backend that drops the refinement.
- Toolchain: `Z3BackendTests.TheContextIsDisposedOnEveryPath` counted exactly one context. The
  refinement opens a second one on an opaque Unknown, and the failing `Assert.Single` then took the
  test host down with 0xC0000005 (native Z3 access violation), not with an assertion failure. The
  test now expects two contexts for a non-timeout Unknown, each disposed once.
- Local environment: `webapi-basic` does not load on this dev box (its legacy Web API packages are
  not restored), so its integration cases fail locally both before and after this change. It has no
  Unknown result, so its snapshot does not change.
