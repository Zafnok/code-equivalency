# P2-100 The same query under the same resource limit ends the same way, whenever the garbage collector runs
Status: todo
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
