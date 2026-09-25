# P1-007 Tested Unknowns: differential execution on generated inputs, with a stated discovery probability
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M4-009; ADR 0035 accepted

## Goal
With `--execute`, every Unknown pair whose parameters M4-009 can construct is run on both runtimes
with generated inputs, until a budget runs out or the estimated discovery probability falls below
a target. It stays Unknown. It gains `properties.differentialTesting`, which says:
- how many inputs ran;
- how many distinct behaviours ("species") were seen, and how;
- the Good-Turing estimate of the probability that the next input shows a species not seen yet
  (Böhme, Liyanage and Wüstholz, FSE 2021). That estimate is an upper bound on the residual risk
  under the generator's input distribution.

An observed divergence is EQ002 with `proofMethod: observed` (ADR 0035 decision 3). This is the
likelihood figure for the part of the code the solver cannot decide. It is labelled as such and
never merged into Equivalent.

## Spec references
ADR 0035 decision 3; ADR 0029 (scope: a `line` Unknown's residual claim is already a proof and is
reported next to the testing figure, not replaced by it); VERIFICATION-MODEL.md sections 1 and 6.

## Design
- **Species.** For each input, the species is the triple (legacy outcome class, modern outcome
  class, IR path signature):
  - an outcome class is the exception type, or `returned` plus a hash bucket of the canonical
    return value (16 buckets);
  - the IR path signature is the sequence of block ids `IrInterpreter` visits on each side, up to
    the first opaque node or abstraction.

  This needs no instrumentation of user assemblies. The report names this definition
  (`species: outcome+irPrefix`), so a later coverage-guided definition is a different, comparable
  label.
- **Estimate.** After n inputs, with f1 the number of species seen exactly once, the discovery
  probability is estimated as f1 / n. Report it with the adaptive-bias caveat from the paper: the
  generator is not adaptive here, so plain Good-Turing applies. Stop when the estimate is below
  `--test-target` (default 0.001) with at least 1,000 inputs, or at `--test-budget` (default
  10,000 inputs or 60 s per pair, whichever comes first).
- **Inputs.** Seeded first with every candidate counterexample the solver produced for the pair
  (ADR 0026 `candidateCounterexample`), then with M3-032's generators. Culture: invariant, plus
  `tr-TR` if any runtime-changes row is reached.
- **Batching.** One driver process per side per pair, with inputs streamed on stdin. The per-case
  timeout is inherited from M3-032.

## Acceptance criteria (all must hold; nothing beyond them)
1. With `--execute`, each constructible Unknown pair gets `properties.differentialTesting`:
   `{ inputs, species, singletons, discoveryProbability, speciesDefinition, stoppedBy: target|budget,
   distribution: "equiv generators v1" }`. Each non-constructible one gets
   `{ notConstructible: <reason> }`.
2. An observed divergence on an Unknown pair produces EQ002 with `proofMethod: observed`, the input
   as `properties.model`, and both canonical outcomes in the message. Its result fingerprint
   follows M1-004's rules for a verdict change (baseline `new`).
3. The message of a tested Unknown ends with one sentence:
   `Tested on <n> inputs; estimated chance the next input shows new behaviour: <p> (equiv generators, not a proof).`
4. Equivalent is never produced by this path. The property test `TestingNeverYieldsEquivalent`
   runs the path over the M0-012 generator's pairs.
5. `--test-target` and `--test-budget` exist, are validated (exit 3 on nonsense), and are no-ops
   without `--execute`.
6. VERIFICATION-MODEL.md sections 1 and 6 are updated: `observed`, `differentialTesting`, and the
   sentence "execution never proves".
7. The `business-layer` sample run with `--execute` gets a checked-in snapshot, with a fixed seed
   so the snapshot is stable.

## Files
`src/Equiv.Execute/Testing/*` (new), `src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/*` (options),
`src/Equiv.Core/Reporting/SarifReportWriter.cs`, `src/Equiv.Core/Ir/IrInterpreter.cs` only if the
path signature is not already observable, `docs/VERIFICATION-MODEL.md`, tests, one snapshot.

## Tests
`GoodTuring_EstimatesFromSingletons`, `StopsAtTarget`, `StopsAtBudget`, `SeedsWithCandidateCounterexamples`,
`ObservedDivergence_IsEq002Observed`, `TestingNeverYieldsEquivalent` (property), `Options_NoOpWithoutExecute`,
`Message_EndsWithTestedSentence`.

## Size guard
Coverage instrumentation, or a fuzzing package, means you are past this ticket. Stop and propose
an ADR 0002 row.

## Out of scope
Heap-carrying inputs (object graphs). Adaptive (coverage-guided) generation. Extreme-value
estimators (Baez et al., ASE 2025), which are a possible later comparison. Changing exit codes.

## Notes
