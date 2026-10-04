# P2-129 A ladder property test fails at random when its generator runs out of procedures that loop and call
Status: done (PR #397)
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`LadderPropertyTests.ALoopingMutantIsNeverEquivalentAndEveryDivergenceReplays` failed once on CI, in
`gates (ubuntu-latest)` of PR #390 (run 37181486781, head 53fba46), a PR that changes nothing in
`src/Equiv.Verify.Z3` or `src/Equiv.Core`. No assertion failed. CsCheck reported
`Set seed: "a6-aU7haBK73" ... (0 shrinks, 24 skipped, 200 total)` and `Failing Where max count`: a
`Where` filter of the test's generator rejected 100 draws in a row (`Check.WhereLimit`), and CsCheck
gives up there. The test has no fixed seed, so this happens at random.

When this is done the test's generator cannot reach that limit in practice, and the property checks
what it checked before: the same procedures, the same mutants, the same assertions, 200 samples.

## Spec references
`docs/VERIFICATION-MODEL.md` section 7 (the soundness harness), `docs/tickets/done/M3-002-*.md`
(criteria 2 and 5, which these properties are), `tests/Equiv.TestSupport/IrGen.cs`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `## Notes` records which of the generator's filters ran out under `CsCheck_Seed=a6-aU7haBK73`, and
   the measured share of draws each filter in `LadderPropertyTests.cs` keeps.
2. A test in `LadderPropertyTests` runs the property on seed `a6-aU7haBK73`. It fails with
   "Failing Where max count" before the fix and passes after it.
3. Every filter in `LadderPropertyTests.cs` that keeps fewer than 1 draw in 5 is replaced by a generator
   that draws the same procedures with the same likelihood and cannot reject 100 times in a row in
   practice. The assertions, the sample counts and the solver budgets do not change.
4. `## Notes` says whether the file's properties are meant to run without a fixed seed, and why.

## Files
`tests/Equiv.Verify.Z3.Tests/LadderPropertyTests.cs`, this ticket, `docs/ROADMAP.md`.

## Tests
`LadderPropertyTests.TheCallingMutantGeneratorDoesNotRunOutOnSeedA6AU7haBK73`, and the file's
existing properties.

## Size guard
A change under `src/`, a change to `IrGen`, or a change to `Check.WhereLimit` (process-wide state)
means the ticket has been misread.

## Out of scope
Property tests in other files. The stream of `CallFreeLooping`, which a pinned seed (P2-059) depends on.

## Notes
- Criterion 1. Reproduced on Windows at 0dabc20 with `CsCheck_Seed=a6-aU7haBK73`: the test fails on its
  first sample. (Locally the message is a `NullReferenceException` from the test's `print`, which CsCheck
  calls with no value because the generator threw; the cause is the same.) The generator stacks three
  filters. With a counter on each, under that seed, the longest run of rejections was: procedure both
  loops and calls, 100 and more; mutant kept, 4; both runs terminate on the witness, 1. So the filter
  that ran out is not the termination filter the test applies itself but the innermost one, `CallingLooping`.
- Measured shares, unseeded: of 20,000 draws of `IrGen.Procedure`, 3,623 loop (18%) and 2,403 loop and
  call (12%); of 20,000 of `IrGen.CallFreeProcedure`, 4,836 loop (24%). Of 5,000 mutations of looping
  calling procedures, 2,278 are kept (46%), and of those 1,131 terminate on both sides (50%).
- What that makes the failure rate. A filter that keeps 12% rejects 100 in a row with probability
  0.88^100, about 3 in a million per draw. The property draws about 880 procedures for its 200 samples,
  so it fails about once in 400 runs. The 18% filter fails about once in a million runs, the 24% filter
  and the two mutant filters never.
- Decision: draw procedures in batches of 16 and keep the first that passes (`FirstOf`), rather than
  build "loops and calls" into `IrGen`. The first passing draw of independent draws has exactly the
  distribution `Where` gives, so the property samples what it sampled before; planting a loop and a call
  in the generated program would change which procedures the property sees (always a top-level loop,
  always a call outside one). A rejection is now a whole batch with nothing to keep (13% for the 12%
  filter, 4% for the 18% one), and 100 of those in a row is below 1e-80. Generating a procedure takes
  about 12 microseconds, so a batch costs nothing next to one solver query.
- Decision: `Check.WhereLimit` is not raised. It is a static field of CsCheck, shared by every test class
  in the process.
- Criterion 3: the two filters under 1 in 5 are `CallingLooping` (12%) and the plain looping filter of
  `ALoopingProcedureIsEquivalentToItself` and `NoRungRefutesAPairAnotherRungProves` (18%); both now go
  through `FirstOf`. `CallFreeLooping` (24%) stays a `Where`: it is not at risk, and
  `NoRungProvesTheMutantOfSeed4FfExD8adOs4` (P2-059) replays one mutant from this generator's stream,
  which a change to the generator would replace with another.
- Criterion 4: the properties are meant to be unseeded. A fixed seed with `iter: 200` would run the same
  200 pairs on every CI run; unseeded, every run of the gate is new evidence, and that is how P2-041
  (`0fgMMTPvcRt7`) and P2-059 (`4FfExD8adOs4`) were found. The convention in this file is to pin a seed
  that found something as its own `iter: 1` test and leave the property unseeded. The seeded tests
  elsewhere (`LoweringOracleTests`, `DifferentialSoundnessTests`) compile or execute a fixed batch and
  are seeded so the batch is the same on every run. So no seed is added here.
