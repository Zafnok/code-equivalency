# P2-133 Mutation testing runs as parallel shards, so a project takes as long as its slowest shard
Status: done (PR #416)
Effort: M
Model: Sonnet, high effort. If you are not Sonnet, Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M0-011

## Goal
In `.github/workflows/mutation.yml` the `stryker` job has one matrix leg per `src/` project, and a leg mutates
every file of its run on one `ubuntu-latest` runner. When a PR changes central files of `src/Equiv.Verify.Z3`
(`FragmentEncoder.cs`, `ProductEncoder.cs`, `LoopLadder.cs`, `TraceEncoder.cs`), nearly every test of
`tests/Equiv.Verify.Z3.Tests` covers every mutant, and the leg takes hours: 1 h 22 min (P1-032), 2 h 33 min and
2 h 55 min (P2-100), over 5 h 16 min on PR #414 (P1-038). GitHub cancels a job at six hours, and the job has no
`timeout-minutes`. The nightly full sweep already ends that way: the `Equiv.Verify.Z3` leg was cancelled at six
hours on 2026-09-30 and 2026-10-05, the `Equiv.Frontend.CSharp` leg on 2026-10-01.

When this is done a project's files are split into shards that run as parallel jobs, a final job per project adds
the shards up under the existing required check name, and wall-clock is about the slowest shard.

## Spec references
`docs/QUALITY-GATES.md` (Mutation row, "Reusing a pass on unchanged code", "Required checks"),
`.github/stryker-pr-config.json`, `.github/scripts/code-fingerprint.sh`, ticket M0-011.

## Acceptance criteria (all must hold; nothing beyond them)
1. A `plan` job computes, per project, the `src/<project>/**/*.cs` files the run mutates: on `pull_request` the
   files that differ from `HEAD^1`, on `workflow_dispatch` from the merge base with `origin/main`, on `schedule`
   every tracked file. It splits them into at most 4 shards (8 on `schedule`), one file per shard when there are
   no more files than shards, and emits the matrix as JSON. A project with no file emits no shard.
2. A `stryker-shard` job runs over that matrix and passes Stryker one `--mutate` glob per file, with the existing
   `--test-runner mtp`, `CsCheck_Threads: 1`, `--concurrency 4` and, off `schedule`, `--since` and
   `--config-file`. It uses `--break-at 0`, adds `--reporter json`, uploads the JSON report as an artifact and
   has `timeout-minutes: 180`. A Stryker run that ends without a report is started once more, because every shard
   repeats the initial test run and a test host that dies in it is the usual way a leg fails.
3. The final job keeps the check names exactly: `stryker (Equiv.Core, Equiv.Core.Tests)` and the five others
   `docs/QUALITY-GATES.md` lists. It sums Killed, Timeout, Survived and NoCoverage over the project's shard
   reports, computes Stryker's score (detected over detected plus undetected) and fails below 90. It fails when
   a planned shard has no report. With no shard, or no mutant tested, it passes.
4. The pass-reuse cache still works: `plan` looks a project's pass up before planning its shards, and the final
   job records it. A prose-only push to a PR whose Stryker legs passed runs no shard.
5. `docs/QUALITY-GATES.md` describes the sharding in the Mutation row and states, in the required-checks list,
   that the names are unchanged.
6. `## Notes` holds, for a change of several `Equiv.Verify.Z3` files: wall-clock and runner minutes before and
   after, and the minutes each shard spends on the build and the initial test run. It also records what was
   found about `--mutate` with `--since` under the MTP runner, the JSON report's shape, and a shard with no
   mutants.

## Files
`.github/workflows/mutation.yml`, `.github/scripts/mutation-plan.sh` (new), `.github/scripts/mutation-score.sh`
(new), `docs/QUALITY-GATES.md`, `docs/ROADMAP.md`, this ticket.

## Tests
No `src/` or `tests/` file changes. The proof is workflow runs: this PR's own run (no `.cs` change: six passing
`stryker` checks and no shard), and a dispatched run on a branch that carries PR #414's five
`Equiv.Verify.Z3` files (four shards, one summed score).

## Size guard
A change to a test, to the 90 threshold, to `.github/stryker-pr-config.json` or to any file under `src/` means
the ticket has been misread: stop.

## Out of scope
Changing the 90% threshold. Changing any test. Changing branch protection or the ruleset. Splitting one file
across shards (Stryker's `--mutate` accepts character spans, `File.cs{0..5000}`, if one file alone ever takes
too long). Making the mutation run itself cheaper (the tests that time out while coverage is captured and are
then run against every mutant).

## Notes
- Decision: where the earlier-pass lookup happens -> one `gh api .../actions/caches?key=&ref=` call per project
  inside the plan script, with `actions: read` on the `plan` job. Alternatives: six `actions/cache/restore`
  `lookup-only` steps in `plan`; a seventh job per project. Rule: 4 (one loop, no step per project). The entry is
  saved by `actions/cache/save` under the PR's merge ref as before, so the key is unchanged and passes recorded
  before this ticket are still found. A failed lookup counts as no pass.
- Decision: how files are balanced -> largest first into the lightest shard, by bytes. Alternatives: round-robin
  by name; by line count. Rule: 4. Nothing knows mutants times covering tests before the run; bytes are the
  cheapest stand-in, and the split is the same on a re-run.
- Decision: the `--mutate` glob -> `**/<project>/<path under the project>`, one per file. Alternative:
  `**/<file name>`, which also matches a file of the same name in another folder. Rule: 1 (Stryker matches the
  glob against the file's full path).
- Decision: the nightly sweep -> the same three jobs, with every tracked file and 8 shards, because it took one
  expression: the plan lists `git ls-files` where a PR lists `git diff`. Alternative: leave it on one runner per
  project, where `Equiv.Verify.Z3` no longer finishes (above) and `Equiv.Frontend.CSharp` takes five hours.
  Rule: 4. Small projects pay for it: `Equiv.Execute` and `Equiv.Cli` build eight times for a sweep one runner
  did in two and five minutes.
- Decision: deleted files -> left out of the plan (`--diff-filter=d`). The old scope step counted a deletion as
  a change and started Stryker with nothing to mutate.
- Decision: a shard whose Stryker run ends without a report -> started once more, then failed. Alternatives:
  fail at once and leave it to a manual re-run; retry any number of times. Rule: 4. The first sharded run lost
  shard 3 of 4 five minutes in to "Initial testrun has more than 50% failing tests" (one test, `State: error`),
  while the three other shards passed the same initial run on the same commit. Three of the last four failed
  `mutation.yml` runs before this ticket ended with that message (`Equiv.Verify.Z3` once, `Equiv.Frontend.CSharp`
  twice), so four shards meet it about four times as often as one runner. Nothing is scored without a report,
  so the retry cannot hide a low score. Why the test host dies in the initial run is not looked into here.
- Checked in Stryker.NET's source at tag `dotnet-stryker@5.0.0`, before any run:
  - `--mutate` with `--since`: `MutantFilter`'s enum order is the order filters run in, and `FilePattern` comes
    before `Since`. `BroadcastMutantFilter` hands each filter only the mutants the one before kept, and marks
    the rest Ignored. So the since filter only ever sees mutants in the shard's files, and its "file changed, set
    Pending" branch cannot bring back a mutant the mutate filter dropped. Neither filter looks at the test
    runner. `FilePatternMutantFilter` matches a glob against the full path and the relative path.
  - The JSON report (`JsonReporter`, `JsonMutant`): `<output>/reports/mutation-report.json`,
    `files.<path>.mutants[]` with `status` one of `Killed`, `Survived`, `NoCoverage`, `Timeout`, `CompileError`,
    `RuntimeError`, `Ignored`, `Pending` (the `MutantStatus` name).
  - The score (`ProjectComponent.GetMutationScore`): Killed plus Timeout, over those plus Survived plus
    NoCoverage. `StrykerRunResult` breaks when the score is under `break / 100` and never on NaN.
  - No mutants to test (`StrykerRunner`): the "all mutants were ignored" path still calls
    `reporters.OnAllMutantsTested`, so the report is written. The upload step fails a shard that has none.
- Measured 2026-10-06 on PR #414's head (`9aff42da`: `FragmentEncoder.cs`, `LoopLadder.cs`, `PositionalTrace.cs`,
  `ProductEncoder.cs`, `TraceEncoder.cs`; 655 tests, 2,607 mutants created, 439 to test).
  Before, one runner (run 37384383087): 15.5 min from job start to the first mutant, then cancelled by GitHub at
  6 h 01 min with the 439 unfinished. At least 361 runner minutes and no result.
  After, four shards (run 37413115577, a dispatch on a branch holding this workflow and that head):

  | Shard | Files | Mutants tested | Job start to first mutant | Whole job |
  |---|---|---|---|---|
  | 1/4 | `FragmentEncoder.cs` | 161 | 8.3 min | 84 min |
  | 2/4 | `LoopLadder.cs` | 102 | 15.4 min | 92 min |
  | 3/4 | `ProductEncoder.cs` | 84 | 6.7 min | 48 min |
  | 4/4 | `TraceEncoder.cs`, `PositionalTrace.cs` | 92 | 8.6 min | 56 min |

  Wall-clock is the slowest shard, 92 min. The four jobs are 280 runner minutes, of which 39 are the build, the
  initial test run and the coverage capture, repeated per shard (build about 30 s, initial run 3 to 4 min,
  capture 4 to 10 min): 23.5 min more than the one runner paid. The sum: killed 382, timeout 54, survived 0, no
  coverage 0, score 100%. Each shard's log has about 2,100 mutants "Removed by mutate filter" and its own "will be
  tested because: Mutant changed compared to target commit", and 161 + 102 + 84 + 92 is the one runner's 439, so
  `--mutate` and `--since` hold together under the MTP runner as the source says.
- Shard 3 is the second attempt. Its first died five minutes in (the retry Decision above), and the project's
  `stryker` job failed with "4 shards were planned and 3 reported" while the five other `stryker` jobs passed.
  "Re-run failed jobs" then ran shard 3 and that one job again, and the job found all four reports: a re-run
  reads the earlier attempt's artifacts.
- 54 of the 436 detected mutants are timeouts, each costing a full timeout and a test-server restart. That is the
  larger part of every shard's time and is not touched here.
- A project's `stryker` job needs the whole `stryker-shard` matrix, so a project with no shard reports when the
  slowest shard of any project ends, where its leg used to pass in a minute. A job cannot need part of a matrix.
- The skipped `stryker-shard` job of a run with no shard is listed under its unexpanded name
  (`stryker-shard (${{ matrix.src }}, ...)`). It is not a required check.
- The workflow and scripts as merged (the retry and the Sonar edits came after the measurement) ran in run
  37511521916: a dispatch with a one-comment edit in each of `Equiv.Verify.Cvc5`'s three files. Three shards
  tested 9, 0 and 15 mutants. The shard with none (`Cvc5Process.cs`, excluded from coverage) logged "all mutants
  with tests were ignored" and still wrote its JSON report, as the source says. The sum was killed 22, survived 2,
  score 91.66%, and the check passed. The retry itself has not fired in a run yet.
