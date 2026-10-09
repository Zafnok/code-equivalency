# P2-148 The second-solver print property fails at random when Z3 is slower on the printed text than on the query
Status: done (PR #449)
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`SecondSolverPrintTests.Z3AnswersTheRewrittenTextAsItAnswersTheOriginal` (ticket P1-033) has failed
twice on CI, on PRs that cannot reach it:

- the PR of P2-127, CsCheck seed `b1l7BGrSxcc1`; it passed on the re-run;
- PR #438, a PR of two Markdown files, check `sonar`, run 37842367616 attempt 1 (job 113534668727,
  `ubuntu-latest`, head 31f737b), seed `eqfiMtEK9Fh6`, `(0 shrinks, 0 skipped, 200 total)`, the test
  taking 1m 17s.

This is not a disagreement between Z3 on the query and Z3 on the printed text, and it is not a
soundness matter for the second-solver path. In both failures the in-process query is `unsat` and the
printed text is `unknown` with reason `timeout`: `ScriptedSolver.Pipeline` gives up after 60 seconds,
and Z3 needs 149 and 203 seconds for the two texts. Given the time, it answers `unsat` for both, as
it does for the queries (Notes). No run of either seed gave `sat` on one side and `unsat` on the other.

The assertion that fails is the second of the pair, line 329,
`Assert.Equal(original == Status.UNSATISFIABLE, answer is SmtUnsat)`. The test returns early when the
in-process answer is `UNKNOWN`, but treats an `SmtUnknown` on the text as a wrong answer. The test has
no fixed seed (by the convention P2-129's Notes record), so a pair whose text takes over a minute
turns up at random.

When this is done a sample on which Z3 gives up on the text is treated as a sample on which Z3 gives
up on the query is treated today: it says nothing about the rewrite, and it is not a failure. A `sat`
against an `unsat` still fails, in either direction.

## Spec references
`docs/adr/0050-cvc5-is-asked-the-rung-1-queries-z3-gives-up-on.md` (decision 2, the two rewrites
this property checks), `docs/tickets/done/P1-033-cvc5-answers-the-rung-1-queries-z3-gives-up-on.md`
(the property is the ticket's),
`docs/tickets/done/P2-129-ladder-property-generator-runs-out.md` (Notes, criterion 4: properties in
this project stay unseeded, and a seed that found something is pinned as its own `iter: 1` test),
`tests/Equiv.Verify.Z3.Tests/ScriptedSolver.cs` (`Solve`, `Pipeline`).

## Acceptance criteria (all must hold; nothing beyond them)
1. In `Z3AnswersTheRewrittenTextAsItAnswersTheOriginal`, a sample whose text answer is `SmtUnknown`
   is not a failure and is not counted in `sent`. `Assert.InRange(sent, 100, 200)` stays, so a run in
   which most texts are given up on still fails.
2. A sample whose text answer is `SmtSat` when the query is `UNSATISFIABLE`, or `SmtUnsat` when the
   query is `SATISFIABLE`, still fails, and the read-back of a `sat` answer is still checked.
3. The same holds for `TheConstantArrayRewriteKeepsTheAnswer` if its
   `ScriptedSolver.Solve(printed.Script) is SmtSat` comparison can fail the same way; Notes say
   whether it can (its terms are 2-bit, so probably not) and what was done.
4. `ScriptedSolver.Pipeline`'s 60 seconds, the 200 samples and the generator do not change.
5. `## Notes` records, over at least 20 unseeded runs of the test after the change, how many samples
   of 200 were dropped because the text was given up on, and the smallest `sent`.
6. No test pins either seed with the solver's full budget: each would add one to four minutes to the
   suite to assert a timeout. If a seed is pinned, it is with a budget of a second or two and asserts
   only that the answer is not `sat`.

## Files
`tests/Equiv.Verify.Z3.Tests/SecondSolverPrintTests.cs`, this ticket, `docs/ROADMAP.md`.

## Tests
`SecondSolverPrintTests.Z3AnswersTheRewrittenTextAsItAnswersTheOriginal`,
`SecondSolverPrintTests.TheConstantArrayRewriteKeepsTheAnswer`.

## Size guard
A change under `src/`, a change to `IrGen`, a change to `ScriptedSolver.Pipeline`'s tactics or time
limit, or a fixed seed on the property means the ticket has been misread.

## Out of scope
Why Z3 is 2 to 26 times slower on the text than on the terms it was printed from. The text holds the
same assertions, read back into a new context. Production does not depend on it: the text goes to
cvc5 (ADR 0050), not back to Z3, and `ScriptedSolver.Solve` exists only in tests. Also out of scope:
why a procedure against itself costs Z3 8 to 85 seconds when it holds a 32-bit multiplication or
division.

## Notes
- Measured 2026-10-08 on Windows (24 logical processors) at 46c34ce, Release build, running only this
  test (`Equiv.Verify.Z3.Tests.exe -method "*Z3AnswersTheRewrittenTextAsItAnswersTheOriginal"`) with
  `CsCheck_Seed` set, and a temporary probe test that draws the seed's pair with `iter: 1`, then
  times the in-process check and the check of the printed text and writes each status and
  `ReasonUnknown` to a file. The probe is not committed.
- Both seeds reproduce on Windows on the first try, and both fail at line 329, as the CI log of
  run 37842367616 shows for Linux (`SecondSolverPrintTests.cs:329`). CsCheck's seed fixes the first
  sample (P2-080), which is the failing one; the test took 101 seconds under `eqfiMtEK9Fh6` and 110
  under `b1l7BGrSxcc1`.
- Line 328 passes and line 329 fails, so the query is `UNSATISFIABLE` and the answer is neither
  `SmtSat` nor `SmtUnsat`. The probe confirms it, three runs in a row in one process, one thread, the
  machine otherwise idle, 60-second limit on both sides:

  | seed | run | query | text |
  |---|---|---|---|
  | `eqfiMtEK9Fh6` | 0 | unsat, 7.8 s | unknown (`timeout`), 60.1 s |
  | `eqfiMtEK9Fh6` | 1 | unsat, 7.9 s | unknown (`timeout`), 60.1 s |
  | `eqfiMtEK9Fh6` | 2 | unsat, 8.5 s | unknown (`timeout`), 60.1 s |
  | `b1l7BGrSxcc1` | 0 | unsat, 43.1 s | unknown (`timeout`), 60.1 s |
  | `b1l7BGrSxcc1` | 1 | unsat, 54.6 s | unknown (`timeout`), 60.1 s |
  | `b1l7BGrSxcc1` | 2 | unknown (`timeout`), 60.1 s | unknown (`timeout`), 60.2 s |

- With the limit raised in the probe (540 seconds for `eqfiMtEK9Fh6`, 280 for `b1l7BGrSxcc1`), one
  run each: `eqfiMtEK9Fh6` query unsat in 14.4 s, text unsat in 203.4 s; `b1l7BGrSxcc1` query unsat in
  84.9 s, text unsat in 148.7 s. So the two sides agree, and the text is 14 to 26 times slower for
  the first seed and about twice as slow for the second.
- Is it deterministic or machine load? For `eqfiMtEK9Fh6` it is deterministic on this machine: the
  text needs over three times the limit, on an idle machine, every time, and the query is far inside
  it. For `b1l7BGrSxcc1` the text side is deterministic too (2.5 times the limit), but the query
  side sits at the limit: 43, 55, over 60 and 85 seconds in four runs with nothing else running. When
  the query also times out the sample returns early and the test passes, which is one way a re-run
  passes. The other is that a re-run without the seed draws other pairs.
- Both failing pairs are from the first branch of the generator, a procedure against itself
  (`(Old: p, New: p)`). `eqfiMtEK9Fh6` multiplies a constant by a value read from the map
  (`overflows smul` and `mul`, 32 bits) and adds it into the result; `b1l7BGrSxcc1` has `udiv`,
  `sdiv`, `srem` and two `urem` on 32 bits. The two sides are encoded under different names, so the
  solver has to prove a multiplier or divider equal to its copy unless simplification merges them
  first. Why it is slower from text was not measured (Out of scope).
- Neither text was rewritten by the constant-array rewrite: `const-array!` and `as const` appear 0
  times in either script, and each script is 133 assertions as a plain solver prints them. So the
  slowdown is in Z3 solving the same assertions read back from text, not in ADR 0050's second rewrite.
- Rate. 12 runs of the test alone without a seed, one after another: 11 passed and 1 failed, at line
  329 again, with the new seed `0001ORmWmt4j` (not replayed). Run times in seconds: 99, 3, 62, 63, 63,
  60, 3, 55, 119 (the failure), 3, 24, 4. So about one run in 12 fails here, from a sample too small
  to pin it (1 of 12 is consistent with anything from 1 in 4 to 1 in 100). Seven of the 12 runs took
  about a minute or more, which is one solver call reaching the limit; in the six that passed it was
  presumably the query's, which the test skips. That was not measured per sample.
- Not measured: Linux timings (the CI log gives only the test's 1m 17s, which fits a 60-second
  timeout on the text after a query answered in seconds); the two seeds under machine load; whether
  cvc5 answers either text.
- Change: a text answer of `SmtUnknown` returns before `sent` is counted (no seed pinned, so criterion 6
  holds trivially). `sat` against `unsat` in either direction, and the read-back, are unchanged.
- `TheConstantArrayRewriteKeepsTheAnswer` cannot fail this way: its terms are 2-bit arrays, it already
  asserts the query is not `UNKNOWN`, and nothing there came near the 60-second limit. Left unchanged.
- Measured after the change (Windows, Release, 20 unseeded runs of the test alone, with a temporary
  stderr counter, removed): 20 passed; 0 samples of 200 dropped because the text was given up on in
  any run (the two failing pairs did not turn up, as expected at roughly 1 run in 12); `sent` was 198 to
  200, smallest 198 (the gap to 200 is queries Z3 gave up on or pairs with nothing to print).
  So the drop path was not exercised by these runs; it is a three-line early return.
