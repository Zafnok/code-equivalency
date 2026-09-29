# P1-010 Caller-sufficient callee contracts: a changed callee the caller cannot observe stops being an unproven assumption
Status: done (PR #265)
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
- **Using K.** In the caller's product program, each side's call to f gets its own fresh result,
  `threw` flag and post-call heap (r, h for the legacy side, r', h' for the modern side; K may
  mention either `threw`), constrained by
  K(args, heapIn, r, h, r', h') when the two calls are aligned with equal arguments and heap. If
  the calls are not aligned, or their arguments may differ, fall back to today's encoding for that
  call. Do **not** use the shared uninterpreted function, or the shared `threw`, under K: that
  would assert r = r' (or equal throwing), the assumption being removed. The call itself stays in
  section 1's call-sequence observable.
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
   removed from `unprovenAssumptions`. The callee's own `unprovenAssumptions` are added to the
   caller's (ADR 0036: K's proof assumes f's matched callees equivalent). A test chains
   C → f → g with g Divergent and asserts g appears in C's `unprovenAssumptions`.
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
`ContractCallee_UnprovenAssumptionsAreInherited`,
`CalleeChangedInvisible_EquivalentPlusContract` (snapshot), `CalleeChanged_Unaffected` (snapshot).

## Size guard
Transitive contracts, contracts for opaque callees, or unaligned calls under K mean you are past
this ticket. Stop.

## Out of scope
Recursion and call cycles (ADR 0019's clarification stands). Caching contracts across runs.
Contracts as user input.

## Notes
- Decision: the backend gets the callee bodies through a second `IVerificationBackend` member,
  `VerifyUnderContracts(old, new, callees, options)`, returning the caller's `Equivalent` with
  `ContractsUsed` or null. `Equivalent.ContractsUsed` (`ContractUse(Callee, Contract, ProposedBy)`) carries
  the contracts to SARIF. The orchestration lives in `CompareCommand.WithContracts`, after
  `WithAssumptions`, and re-verifies only Equivalent results (congruent ones included) whose unproven
  assumptions include lowered callee pairs. The Files list names none of `Equiv.Core`'s interface, the
  verdict record or the CLI, but criterion 4 cannot be met without them.
- Decision: the fresh-per-side encoding reuses the side-specific functions a runtime-changed callee
  already gets (`TraceEncoder`'s `:old`/`:new` suffix). The replay oracle then answers from the same
  functions and needs no change. `ICalleeContractEncoding` is the seam criterion 3 swaps;
  `FreshPerSideEncoding` is its only production implementation. `Equiv.Tests.Integration` joins
  `Equiv.Verify.Z3`'s InternalsVisibleTo for that (CLAUDE.md's exception for internal contracts).
- Decision: K is a list of conjuncts (`threw` agrees; exception type agrees when both throw; calls
  agree; each heap map agrees; each observed predicate agrees unless either side throws). Only
  conjuncts the caller's product can see fail are droppable. A model that falsifies exception type,
  calls, or a heap map the caller does not name ends the search. The caller encodes a call's exception
  as `System.Exception` and never sees its callee's calls, so dropping those conjuncts would be unsound.
- Decision: in a callee pair's product under K, the call traces must always agree as well as K. Found
  while testing: lockstep's segments compare header states through cut events in the trace, and a K
  without the trace conjunct let rung 2 "prove" `loops/warm-up`.
- Decision: observed predicates are Bool definitions whose backward slice through `const`, binary,
  unary and `mapread` reaches only this call's result, the heap maps it left, and the caller's
  synthesised `In` inputs. A predicate reaching a source parameter or a phi is not one. That keeps K a
  function of the call's outcome alone. Duplicates are merged by their SMT-LIB text.
- Decision: `ContractVerifier` runs rung 1 on the pair unrolled, then rungs 2 and 3 for a looping pair.
  It skips rung 1's "no input reaches the bound" query, since rungs 2 and 3 decide what it would.
  Rungs 4 and 5 build their own query and are not asked. `MaxRounds` is 4 checks.
- Decision: a callee pair with an `IrOpaque`, a source parameter passed by reference, differing
  return types, a self-call or irreducible flow gets no contract.
- Decision: a throw from `VerifyUnderContracts` keeps the caller's verdict and prints a
  `warning:` line on stderr. There is no SARIF notification: the verdict left is sound, unlike a pair
  whose verification crashed (ADR 0023). The contract step has no progress phase of its own.
- Decision: a callee whose contract is used stays in `assumedCallees`, and its inherited unproven
  assumptions are added to `assumedCallees` too, so `unprovenAssumptions` stays a subset of it.
- Decision: the contract's SMT-LIB text is Z3's printing of K over named constants, with whitespace
  collapsed to one line. `proposedBy` is `observed-predicates`.
- Deviation: the Design's second candidate source, the P1-002 model proposer, is not wired. No
  acceptance criterion or listed test covers it ("nothing beyond them"), and `proposedBy` is always
  `observed-predicates`. It is left for a follow-up ticket.
- The sample's `Score` is `a > 10 ? a - 1 : a`. A first IR draft used `a + 1`, which wraps at
  `int.MaxValue` and turns the score negative, and Z3 rejected the contract on exactly that input.
- Property budget: 40 triples per PR and 1,000 nightly, via the same `EQUIV_DIFFERENTIAL_BUDGET` and
  `EQUIV_DIFFERENTIAL_SEED` as M0-012. At the default seed, 23 of the first 40 triples are Equivalent
  under a contract, and the naive shared-function encoding hides an executed divergence in 2 of them.
