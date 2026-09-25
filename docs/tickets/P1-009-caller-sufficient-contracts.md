# P1-009 Caller-sufficient callee contracts: a changed callee the caller cannot observe stops being an unproven assumption
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-015, P1-005, P1-002; ADR 0036 accepted

## Goal
Take a caller C that calls a matched callee pair (f, f') which is not Equivalent. Today C is
Equivalent with `unprovenAssumptions` naming f (ADR 0019), or Unknown. Often C uses only part of
f's behaviour: for example C checks only `f(x) > 0`, and f changed only for results that were
already above 10. This ticket:
- derives a relational contract K, such as `(r > 0) == (r' > 0)`;
- proves that the pair (f, f') satisfies K;
- re-verifies C with K in place of the shared function.

If C is Equivalent under K, f moves from `unprovenAssumptions` to `contractsUsed`. This is the
approach of *Partial Contracts Suffice* (Charalambous et al., arXiv 2607.10291, 2026), made
relational so that it fits the product program.

## Spec references
ADR 0036 decision 2; ADR 0018 (calls and the heap); ADR 0019 (assumed callees); ADR 0026 (taint);
VERIFICATION-MODEL.md sections 1 and 6.

## Design
- **Admitting K.** Take the product program of f and f', which the backend already builds when it
  verifies that pair. Replace its "outputs differ" goal with "not K(args, heapIn, r, h, r', h')".
  `unsat` means K is admitted. This reuses the ladder, so loops in f are handled as they are for
  any pair.
- **Using K.** In the caller's product program, each side's call to f gets its own fresh result
  and post-call heap (r, h for the legacy side, r', h' for the modern side), constrained by
  K(args, heapIn, r, h, r', h') when the two calls are aligned with equal arguments and heap. If
  the calls are not aligned, or their arguments may differ, fall back to today's encoding for that
  call. Do **not** use the shared uninterpreted function under K: that would assert r = r', the
  assumption being removed. The call itself stays in section 1's call-sequence observable.
- **Candidates**, cheapest first:
  1. **Observed predicates.** Collect every predicate the caller applies to the call's result or
     to a heap cell the call wrote: branch conditions, comparisons, and `== null`. The candidate
     is the conjunction of `P(r, h) == P(r', h')` over them. If it is rejected, drop the
     conjuncts the counterexample falsifies and retry, within `MaxRounds`.
  2. **The P1-002 model proposer,** if enabled, given the IR of f, f' and C and the rejected
     candidates.
- **Scope.** Direct callees of C only. A callee pair that is opaque, or whose body does not
  lower, gets no contract.

## Acceptance criteria (all must hold; nothing beyond them)
1. `ContractVerifier` in `Equiv.Verify.Z3` takes a callee pair and a candidate K and returns
   `Admitted`, `Rejected(model)` or `Unknown(reason)`.
2. The caller re-verification uses the fresh-per-side encoding in Design. The soundness property
   `ContractNeverHidesAnObservedDivergence` generates (C, f, f') triples with M0-012's generator.
   Whenever concrete execution shows C and C' differ, the caller's verdict is not Equivalent.
3. The pitfall property `SharedFunctionUnderContractWouldBeUnsound` builds the naive encoding (K
   plus the shared function) in the test only, and shows it reports Equivalent on a generated
   triple where execution differs. The test also asserts that the backend wires the
   fresh-per-side encoder.
4. A successful caller result has `proofMethod` suffixed `+contract` (for example
   `lockstep+contract`) and
   `properties.contractsUsed: [{ callee, contract (SMT-LIB), proposedBy }]`, and the callee is
   removed from `unprovenAssumptions`.
5. New sample `callee-changed-invisible`: `Classify(int a) => Score(a) > 0 ? "pos" : "neg"`,
   where `Score` changes only for inputs whose score was already above 10. `Score` is Divergent,
   and `Classify` is Equivalent `+contract` with an empty `unprovenAssumptions`. Its README states
   it, and its snapshot is checked in.
6. The existing `callee-changed` sample (M3-015) keeps its verdicts. Its caller does observe the
   change, so no contract is admitted.

## Files
`src/Equiv.Verify.Z3/Contracts/*` (new), the backend's call-encoding file(s),
`src/Equiv.Core/Reporting/SarifReportWriter.cs`, `samples/callee-changed-invisible/**`,
`docs/VERIFICATION-MODEL.md`, tests, snapshots.

## Tests
`ContractVerifier_AdmitsWhenProductSatisfiesK`, `ContractVerifier_RejectsWithModel`,
`ObservedPredicates_DropFalsifiedConjuncts`, `ContractNeverHidesAnObservedDivergence` (property),
`SharedFunctionUnderContractWouldBeUnsound` (property),
`CalleeChangedInvisible_EquivalentPlusContract` (snapshot), `CalleeChanged_Unaffected` (snapshot).

## Size guard
Transitive contracts, contracts for opaque callees, or unaligned calls under K mean you are past
this ticket. Stop.

## Out of scope
Recursion and call cycles (ADR 0019's clarification stands). Caching contracts across runs.
Contracts as user input.

## Notes
