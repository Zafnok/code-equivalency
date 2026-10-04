# P1-020 A Divergent says whether the modern side only removed failures, added one, or changed a result
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-013

## Goal
An EQ002 carries one counterexample. It does not say whether that input is the whole story. Git
Extensions' "no functional change" pull request added `?? string.Empty`: the legacy side throws on
`null` and the modern side returns. That pair is Divergent, and so is a pair whose modern side returns
a different number. A reviewer wants to tell the two apart before opening either: the first is a
fixed crash, the second is a changed result. Alive2 (Lopes et al., PLDI 2021; citation from memory,
check before relying on it) calls the first a refinement: the new version is defined wherever the old
one was, and agrees with it there.

ADR 0037 already asks the two failure queries on an Unknown (`FailureRefinementQuery`). This ticket
asks them, plus one more, on a Divergent, and reports the three answers. The three large runs hold
465 Divergent results, so the cost is at most three more queries on about 1% of matched pairs.

## Spec references
ADR 0037 (the two failure queries, their opaque rule, their taint rule), ADR 0026 (taint), ADR 0014
(opaque nodes), VERIFICATION-MODEL.md section 6 (`failureRefinement`, the review list),
`src/Equiv.Verify.Z3/FailureRefinementQuery.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `equiv-adr`'s bar test is run first and its outcome is in Notes. The expected vehicle is a dated
   clarification of ADR 0037 that applies its two queries to a Divergent and adds the third query of
   criterion 3. If the bar test says new ADR, the ADR is merged before any code.
2. Every EQ002 whose counterexample came from the solver (not `proofMethod: observed`) carries
   `properties.failureRefinement` with `newFailures` and `removedFailures`, each `{ outcome, model? }`
   with the outcomes and rules ADR 0037 gives them. The queries run over the same product the
   counterexample came from.
3. The same result carries `properties.failureRefinement.resultChanges`, `{ outcome, model? }`: `found`
   when some input makes both sides return, or both throw the same exception type, with another
   compared observable differing, and that model replays untainted on both sides; `none-proved` when
   the query is unsatisfiable under ADR 0037's opaque rule; `unknown` otherwise.
4. `properties.divergenceKind` is derived from the three answers and nothing else:
   - `removed-failures-only`: `removedFailures` is `found`, and `newFailures` and `resultChanges` are
     both `none-proved`;
   - `new-failures-only`: `newFailures` is `found` and the other two are `none-proved`;
   - `failures-only`: both failure answers are `found` and `resultChanges` is `none-proved`;
   - `result-changed`: `resultChanges` is `found`;
   - `undetermined`: anything else.
   The message of a `removed-failures-only` result ends with `The modern side differs only where the
   legacy side throws.`
5. The verdict, rule id, level, exit code, fingerprint and `reviewGroup` of a result do not depend on
   any of these properties. A test pins this on one sample.
6. A pair with a loop or self-call is queried on rung 1's unrolled product, and an input that reaches
   the bound has an unknown outcome, as ADR 0037 has it for an Unknown.
7. VERIFICATION-MODEL.md section 6 describes the properties. `samples/removed-null-check` (a new
   failure) and a new sample `added-null-guard` (a removed failure) have their snapshots checked in,
   with `new-failures-only` and `removed-failures-only`.
8. The census reports the Divergent pairs queried and the time spent, beside the existing
   `loweringCensus.failureRefinement`.

## Files
`src/Equiv.Verify.Z3/FailureRefinementQuery.cs`, `src/Equiv.Verify.Z3/Z3Backend.cs`,
`src/Equiv.Core/` (the verdict record and `SarifReportWriter`), their tests,
`samples/added-null-guard/**`, the two samples' snapshots, `docs/VERIFICATION-MODEL.md`,
`docs/adr/0037-unknown-states-whether-failures-are-new.md` (clarification only), `README.md` (the
paragraph on `failureRefinement`).

## Tests
`DivergentCarriesFailureRefinement`, `ResultChanges_FoundNeedsAnUntaintedReplay`,
`ResultChanges_NoneProvedWhenOnlyTheThrowDiffers`, `DivergenceKind_IsDerivedFromTheThreeAnswers` (one
case per kind), `DivergenceKind_ChangesNoFingerprintOrExitCode`, the two sample snapshots, and a
property test: for a generated pair reported `removed-failures-only`, every generated input on which
the legacy side returns gives equal observables in `IrInterpreter`.

## Size guard
More than three queries per Divergent, or any change to how a result is ranked or grouped: stop.

## Out of scope
EQ006 results (their side-specific functions are free in the model). Observed Divergents. Ranking
`removed-failures-only` groups lower in the review list: file it as a ticket if the first corpus run
with this property shows it would move at least 5% of flagged results. Unknowns on `timeout`
(P1-021).

## Notes
- From the 2026-10-03 improvement review (the "Refinement verdicts" row of its techniques table).
