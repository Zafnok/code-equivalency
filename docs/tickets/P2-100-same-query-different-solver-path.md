# P2-100 The same query under the same resource limit ends the same way, whenever the garbage collector runs
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-050

## Goal
P2-050 bounds every query by Z3's `rlimit`, so a result no longer depends on machine speed or load.
Its measurement (`docs/runs/2026-10-01-timeout-budget.md`, "Repeatability") shows what is left. Two
runs of 184 Git Extensions pairs at the default budget agreed on 179. One of the other five
exhausted the resource limit in one run and found a model in the other, and several pairs took
2.6 to 5.6 times longer to exhaust the same limit from one run to the next, so the solver did
different work on the same query. With the garbage collector's generation 0 budget raised so that
collections are rare, every outcome repeated three times out of three, and the times were steady
until the third run. The likely mechanism: Z3's .NET binding releases a term
when the collector finalises its wrapper (`Z3_enable_concurrent_dec_ref`), Z3 gives a freed term's
id to the next term it builds, and Z3 orders terms by id in several places. So the ids of the
formula handed to a check depend on when a collection happened while it was being built.

Confirm or refute that mechanism, and make the formula a check receives independent of it. Until
then a baseline comparison can still show a `new` result that nothing caused, and P2-077's parallel
verification cannot promise the results of `--jobs 1`.

## Spec references
VERIFICATION-MODEL.md section 6 (solver budgets), `src/Equiv.Verify.Z3/Z3Backend.cs` (`Query`,
`Inline`), `src/Equiv.Verify.Z3/ChcEncoder.cs`, `docs/runs/2026-10-01-timeout-budget.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Measure first. A test or a harness run shows one query whose `rlimit count` at a decided answer
   differs between two runs in one process, and names the cause in `## Notes`: term ids, or
   something else found on the way. If it is not term ids, stop and rewrite criteria 2 and 3 in a
   `Deviation:` line before going on.
2. Every solver and fixedpoint this repo creates checks a formula whose term ids do not depend on
   garbage collection. Two candidate ways, chosen by `equiv-decide` and logged: translate the
   assertions into a fresh `Context` just before the check and translate the model back, or keep
   every wrapper of a context alive until the context is disposed. The assertions Z3 receives are
   the same as before for the fixtures in `tests/Equiv.Verify.Z3.Tests`.
3. Repeatability on the corpus: the 184 pairs of P2-050's measurement, twice at the default budget
   on four threads and twice on one thread. Every pair that the resource limit ends, or that is
   decided, has the same outcome, ladder and detail in all four runs. Recorded in
   `docs/runs/<date>-solver-repeatability.md`, with the pairs the wall-clock backstop ended listed
   apart.
4. No verdict is lost: every fixture and sample keeps its verdict and proof method.

## Files
`src/Equiv.Verify.Z3/Z3Backend.cs`, `src/Equiv.Verify.Z3/ChcEncoder.cs`, their tests,
`docs/runs/<date>-solver-repeatability.md`.

## Tests
`Z3BackendTests.TheSameQuerySpendsTheSameResourceWhateverWasCollected`,
`ChcEncoderTests.TheSameQuerySpendsTheSameResourceWhateverWasCollected`.

## Size guard
A change to which queries a rung makes, to the tactic pipeline, or to a process-wide garbage
collector setting means the ticket has been misread: stop.

## Out of scope
The pairs the wall-clock backstop ends (their time is P2-076's). The value of the defaults (P2-050).

## Notes
- From P2-050: 35 pairs, three runs each on one thread at `resourceLimit` 5,000,000. Default
  collector settings: 34 identical, one pair Unknown(abstraction) once and timeout twice, 7 pairs
  whose times differ by more than 1.5 times. `DOTNET_GCgen0size` raised to 8 GB: all 35 identical in
  all three runs; no pair over 1.6 times in the first two, and four pairs swing by up to 6.6 times in
  the third. The first run in a process, before many collections have happened, tends to match the
  rare-collection runs. This points at collection timing and does not prove it; criterion 1 settles it.
- Measured (criterion 1): the cause is term ids. Each fixture's divergence query was encoded and checked four
  times in one process: twice in an untouched context, once after 200 terms had been made and two thirds of them
  disposed, and once after 200 unreferenced terms had been made and a collection had finalised them. The two
  untouched runs spent the same `rlimit count` on all 60 fixtures. A disturbed run reached the same answer after a
  different count on 18 of them: `call-closed-mixed` 1,830 untouched, 1,152 after the disposals and 1,143 after the
  collection; `loops/chc-uncertified` 35,795 and 44,380 after the collection. A Spacer query differs the same way:
  `loops/fusion` is proved after 2,588,749 units untouched and 1,759,358 after the disposals, which is the count
  that "moves from run to run" in P2-050's notes. Nothing else was found on the way.
- Decision: how a check gets term ids that do not depend on collection -> its assertions are translated into a
  fresh `Context` that holds nothing else (`SolverQuery` for a solver, the same inside `ChcEncoder.Query` for a
  fixedpoint). Alternatives: keep every wrapper of a context alive until it is disposed (the binding makes
  wrappers inside its own calls, for the sorts and declarations of a `Mk` call and for every `Args`, and hands no
  one a way to hold them, so it cannot be done from outside the binding). Rule: 3.
- Deviation: criterion 2's first way says "translate the model back". The .NET binding has no `Model.Translate`
  (the C API's `Z3_model_translate` is not exposed, and a `Model` cannot be constructed). The model stays in the
  fresh context and is read through translation: `SolverModel.Eval` translates the term in and the value out, and
  `SolverModel.Map` does the same for a function's interpretation. So `Model` became `SolverModel` wherever a
  model is read, and the change touches more files than the ticket lists: `SolverQuery.cs` and `SolverModel.cs`
  (new), `ModelDecoder.cs`, `SecondSolver.cs`, `LoopLadder.cs`, `FailureRefinementQuery.cs`,
  `Contracts/ContractVerifier.cs`, and the tests that hand a decoder a model.
- Decision: how the assertions move -> as one conjunction per batch (`MkAnd`, `Translate`, then its `Args`), so
  shared subterms are walked once, and the conjunction's wrapper is held until the query is disposed so that no
  finalizer frees a term of the fresh context while it is in use. Alternatives: `Solver.Translate`, which moves a
  solver in one native call (Z3 5.1 dies with an access violation checking the translated tactic solver, on
  `loops/recursion-unaligned` here, and the translated solver has lost its `rlimit` and `timeout`); one `Translate` per assertion (walks the
  shared subterms of the inlined query once per assertion). Rule: 4.
- Decision: the two batches of `Z3Backend.Query` stay two stages in their old order, `assert` (the encoding) then
  `inline` (the query), each now including its translation. Alternatives: one batch, which swaps the two lines in
  every progress log. Rule: 4.
- Decision: `ChcAnswer.Spent`, the `rlimit count` of a fixedpoint's statistics -> the Spacer test needs the number
  and nothing else carried it. Alternatives: finding the count by bisecting the limit in the test. Rule: 3.
- Decision: `SecondSolver.Asked` makes the queries it holds (`Ask`, `ReadBack`, `Refuted`) -> CA2000 does not take
  a constructor argument as handed over, with or without `dispose_ownership_transfer_at_constructor`, and
  `SolverQuery` is this repo's own disposable, which the rule follows where it did not follow Z3's `Solver`.
  Alternatives: a `try`/`catch` that disposes and rethrows (a branch no test reaches). Rule: 4.
- Observed: the fixture snapshots of the assertions Z3 receives are unchanged, and now print the fresh context's
  solver, which is the one that is checked.
- Observed: each query now holds a second copy of its assertions for as long as it lives. Cost in time on the
  corpus pairs is in the run file.
- Observed: `main` is red without this change, on `SecondSolverLadderTests.TheSolversQueriesAreStages` and one
  `Equiv.Tests.Integration` snapshot (P1-035 and P1-033 each changed what the other pins). Not touched here.
- Observed: three harness processes loading the same two solutions at once each loaded a different part of them
  (7,057, 13,168 and 13,460 of 13,541 pairs). One load at a time
  gives 13,541. A measurement that needs the same pairs in every run loads once.
