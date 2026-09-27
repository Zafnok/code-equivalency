# P1-008 Tested Unknowns: differential execution on generated inputs, with a stated discovery probability
Status: done (PR #230)
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
- **Species.** For each input, the species is the tuple (legacy outcome class, modern outcome
  class, outcomes equal, IR path signature):
  - an outcome class is the exception type, or `returned` plus a hash bucket of the canonical
    return value (16 buckets);
  - outcomes equal is whether the two canonical outcomes are equal. Without it a divergent input
    can fall into a species already seen (two values in one hash bucket, one path prefix), and the
    estimate would no longer bound the chance of the next input diverging. With it, while no
    divergence has been seen, any divergent input is a new species;
  - the IR path signature is the sequence of block ids `IrInterpreter` visits on each side, up to
    the first opaque node or abstraction.

  This needs no instrumentation of user assemblies. The report names this definition
  (`species: outcome+equal+irPrefix`), so a later coverage-guided definition is a different,
  comparable label.
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
- Decision: an observed divergence is a `Divergent` whose `Observed` holds both runtimes' outcomes
  (`Divergent.Observation`). `Verdict` stays closed at five kinds, so every switch that reads EQ002
  (rule id, exit code, baseline) treats it as the Divergent it is. It has no model of the IR, so its
  `Counterexample` is `Divergent.Unmodelled` (no inputs, both runs `infeasible`), which the writer
  and the fingerprint never read: both use `ObservationText` instead. `proofMethod: observed` is
  written by the SARIF writer and is not a `ProofMethod` member, so no `Equivalent` can carry it.
- Decision: `properties.model` of an observed divergence is `inputs(<wire args>) culture(<name>)`,
  and the message is `<id> diverges on the real runtimes: <model> legacy(<kind> <canonical>)
  modern(<kind> <canonical>)`. The fingerprint hashes the message's dump, so a verdict change from
  Unknown is `new` (M1-004).
- Decision: an observed divergence carries no `differentialTesting`. Criterion 1 describes Unknown
  results, and testing stops at the first divergence, where the estimate has no meaning left.
- Decision: a divergent input is rerun once per side in fresh processes before it is reported
  (ADR 0035's "runs every input twice per side", applied only where it matters). If either outcome
  changes it is noise: the input stays a species, with `equal` false, and testing goes on.
- Decision: the species key is `<legacy class> / <modern class>` per culture, `equal <bool>`, then
  `path <legacy blocks> / <modern blocks>`. The outcome class of a timeout or a value with no
  canonical form is `not-comparable`; such an input is never a divergence. A side answering
  `NotConstructible` (its driver could not build the arguments or the culture) stops testing with
  `notConstructible: the <side> side gave NotConstructible <canonical>`, since every later input
  would say the same and the figure would describe nothing.
- Decision: the hash bucket is FNV-1a over the canonical text's UTF-16 code units, modulo 16, so a
  class is the same on every machine (`string.GetHashCode` is randomised per process).
- Decision: the IR path signature is `IrRun.Path`, recorded by `IrInterpreter` on every run and
  ignored by `IrRun` equality (it is not an observable). The path was not observable before, so
  `IrInterpreter.cs` changed, as the Files list allows. For the path, every call and pure function is
  an abstraction and is answered with its type's default (`DefaultOracle`), so the signature stops
  at the first branch on one. Wire arguments bind to the body's C# parameters by position; a
  sort-typed value (string, floating point, decimal, any reference) is one element per distinct wire
  text, `null` also set in the `null.<Sort>` map; the receiver is one more element; every other
  synthesised input is its type's default. The step budget is 1,000.
- Decision: "a runtime-changes row is reached" is read statically: either body holds an `IrCall`
  whose callee is `RuntimeChanged` or a `RuntimeSensitive` `IrPure`. A dynamic reading would miss
  calls inside opaque fragments, which the IR path never enters. Each input then runs under both
  cultures and counts once; its species covers both.
- Decision: inputs come from `InputGenerator.Stream`, which yields `Generate`'s cases and then keeps
  drawing; a parameter with finitely many values (a `bool`, an enum, `null`) draws among them, so a
  finite input space is sampled with replacement and its estimate falls to 0 once every value
  repeats. `Generate` is now `Stream(...).Take(...)`, with the same output as before. The seed is
  fixed at `DifferentialTester.Seed` (0), which is what makes criterion 7's snapshot stable; there
  is no seed option. Seeds from the candidate counterexample count as inputs.
- Decision: the pair is tested with the legacy side's parameter list, position by position, and
  both sides must classify every parameter alike (`the two sides' parameters differ: ...`). An
  enum's values are the union of both sides', as in M3-032.
- Decision: the frontend's part is `IReplayDriverFactory.Plan(pair, candidate, directory)`, beside
  `Create`, returning a `TestingPlan` (Core). It is outside the Files list, but the frontend alone
  holds the symbols. `ReplayArguments.CallObstacle` is the part of replay's obstacle check that does
  not depend on a model, shared by both. The drivers are replay's (`EquivReplay<n>`).
- Decision: `--test-budget` is `<inputs>` or `<inputs>,<seconds>`, both positive integers;
  `--test-target` is a number strictly between 0 and 1. Both are validated by System.CommandLine
  validators, so nonsense is a parse error and exit 3 with or without `--execute`; valid values do
  nothing without it. `CompareCommand.Create` takes an optional `ExecutionEnvironment` so a test can
  run the parsed command against fakes, and `ExecutionEnvironment.Time` is the budget's clock.
- Decision: `run.properties.loweringCensus.unknownByScope` is counted after testing, so an Unknown
  that became an observed Divergent is not counted as Unknown.
- Decision: `TestingNeverYieldsEquivalent` lives in `Equiv.Tests.Integration`, beside the M0-012
  harness it reuses: 30 `PairGen` pairs, 200 inputs each, both sides run in-process on each case
  through a host that decodes the wire line. It also checks that a divergence it observes is on a
  pair the solver did not call Equivalent.
- Decision: the snapshot is `tests/Equiv.Tests.Integration/business-layer.execute.sarif`. It differs
  from `samples/business-layer/expected.sarif.json` by `Describe`'s tested sentence and
  `differentialTesting` (1,000 inputs, 1 species, stopped by target: its only parameter is an
  `Order`, which the generators build only as `null`) and by `RoundTotal`'s M4-009 replay fields.
- Observed: the whole `business-layer` run with `--execute` takes about 7 s on the dev box.
