# P2-134 what thorough mode's later passes cost, per knob: gitextensions-8522 (2026-10-08)

Question: thorough mode's budget pass verifies again at `bound` 8, `resourceLimit` 30,000,000 and `timeoutMs`
600,000, and its IL pass at the same values. What does each value cost, and what does it decide?

**Answer: the bound of 8 is where most of the time goes, and it decides nothing the bound of 3 does not. With the
escalation as it is, thorough takes 25,865 s on this pair against quick's 368 s. With `bound` 3 and the other two
values as they are it takes 10,870 s and decides two more results, because three divergences that show within three
iterations run out of the resource limit when the loops are unrolled eight times. The resource limit of 30,000,000
is the other large cost, and it is what the pass's answers come from: at 5,000,000 the run takes 2,715 s and the
budget pass reports 5 Divergent where it reports 13 to 15 at 30,000,000. No budget pass in any run proved a pair
Equivalent. The IL pass's one proof and its four EQ002 come at every value tried; the larger resource limit adds two
EQ006 to it.**

So the budget pass's `bound` becomes 3 (a clarification of ADR 0049), its `resourceLimit` and `timeoutMs` stay, and
the IL pass keeps the budget pass's values. Thorough then costs what run B below cost: 10,870 s, 30 times quick.

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`). 13,541 matched pairs, 13,742 results in every run.
- Build: `main` at `b68abf40`, Release, for every run. Each run is `tools/corpus/corpus.ps1 -Compare
  gitextensions-8522 -Mode thorough -CompareArgs '--jobs', '4'` (`full` mode of `equiv-corpus-run`), one after
  another, never two at once.
- The first pass is the same in every run: `bound` 3, `resourceLimit` 2,000,000, `timeoutMs` 60,000.
- How each run got its values:

  | run | budget pass | IL pass | set by |
  |---|---|---|---|
  | quick | none | none | `-Mode quick`, for the comparison of criterion 4 |
  | A, the escalation as it is | 8 / 30,000,000 / 600,000 | the budget pass's | nothing: the defaults |
  | B, `bound` 3 | 3 / 30,000,000 / 600,000 | the budget pass's | `--config` with `{ "escalation": { "bound": 3 } }` |
  | C, `resourceLimit` 5,000,000 | 8 / 5,000,000 / 600,000 | the budget pass's | `--config` with `{ "escalation": { "resourceLimit": 5000000 } }` |
  | D, the IL pass at the first pass's values | 8 / 30,000,000 / 600,000 | 3 / 2,000,000 / 60,000 | a one-line patch, below |

  Run D needs a patch because no setting names the IL pass's values. In `CompareCommand.LaterPasses` the IL pass's
  options, `passes.Budget ?? passes.Whole`, were changed to `passes.Whole` (the first pass's values with every
  query on), the CLI was rebuilt, and the change was reverted after the run. The patch is not part of this pull
  request. To reproduce run D, make that one change on `b68abf40`.
- Machine: one Windows box, 24 cores, 63 GB. The run script recorded the `dotnet`, `Equiv.Cli`, `z3` and MSBuild
  processes before each run and every five minutes during it, with the machine's processor load.
  - Before each run: nothing heavy was running (the compiler server and idle MSBuild nodes of the build just made).
  - Run A: from 10:50 to 12:40 other sessions' builds and short `Equiv.Cli` runs ran beside it, and the machine was
    at 100% load for about 75 minutes of the budget pass. In that time the run's process still used 2.7 processors
    against 3.0 in the hour before.
  - Run D: the load was at or above 90% in 29 of 74 samples with no other `dotnet`, `Equiv.Cli` or `z3` process to
    account for it, so something the script does not watch was running.
  - Runs B and C: 3 of 36 and 2 of 9 samples at or above 90%, and no other heavy process.

  Runs A and D have the same budget pass, and theirs took 22,982 s and 21,638 s. That difference, 6%, is the
  size of what the other load and the run-to-run variation did. The differences between the knobs are far larger.
- Every thorough run wrote one warning: verifying `GitExtensions.Plugins.GitImpact.ImpactControl::UpdatePathsAndLabels()`
  again in the IL pass failed (P2-078), so the pair kept its result. No run has a notification or an unverified pair.

## Times

Seconds, from each run's `progress.log`. "Pair time" is the sum over the pass's pairs, on four threads.

| | quick | A as it is | B `bound` 3 | C `resourceLimit` 5,000,000 | D IL at first pass's values |
|---|---|---|---|---|---|
| wall clock | 368 | 25,865 | 10,870 | 2,715 | 22,318 |
| against quick | 1 | 70 times | 30 times | 7 times | 61 times |
| lower | 103 | 107 | 107 | 89 | 84 |
| verify (first pass) | 216 | 214 | 244 | 203 | 205 |
| budget pass | | 22,982 | 9,113 | 1,994 | 21,638 |
| IL pass | | 2,163 | 1,072 | 191 | 28 |
| contracts pass (841 callers) | | 365 | 301 | 205 | 333 |
| budget pass: pairs | | 168 | 168 | 168 | 168 |
| budget pass: pair time | | 89,068 | 34,511 | 7,737 | 84,284 |
| budget pass: median pair | | 79.6 | 32.7 | 9.3 | 76.0 |
| budget pass: nine pairs in ten under | | 2,403 | 482 | 107 | 2,040 |
| budget pass: longest pair | | 5,516 | 3,300 | 790 | 5,318 |
| IL pass: pairs | | 34 | 34 | 35 | 34 |
| IL pass: pair time | | 3,164 | 1,837 | 523 | 79 |
| IL pass: longest pair | | 2,163 | 1,072 | 191 | 27 |
| peak working set sampled (MB) | 3,276 | 32,902 | 30,428 | 9,740 | 34,183 |
| processor seconds of the process | 788 | 67,622 | 29,623 | 9,355 | 61,619 |

The IL pass's wall clock is its longest pair's time in every run: one pair,
`GitUI.CommandsDialogs.FormFormatPatch::FormatPatch_Click(object,System.EventArgs)`, is Unknown at every value and
takes as long as the budget lets it.

## What each pass produced

A result a pass "produced" is one that replaced the earlier result (ADR 0049 decision 2) and carries
`properties.decidedBy`.

| | A as it is | B `bound` 3 | C `resourceLimit` 5,000,000 | D IL at first pass's values |
|---|---|---|---|---|
| **budget pass: results produced** | 72 | 79 | 26 | 72 |
| EQ001 Equivalent | 0 | 0 | 0 | 0 |
| EQ002 Divergent | 1 | 1 | 0 | 1 |
| EQ006 runtime-changed | 12 | 14 | 5 | 12 |
| EQ003 Unknown `abstraction` | 30 | 46 | 10 | 30 |
| EQ003 Unknown `opaque` | 15 | 13 | 6 | 15 |
| EQ003 Unknown `unaligned-loop` | 14 | 5 | 5 | 14 |
| pairs that kept their earlier result | 96 | 89 | 142 | 96 |
| **IL pass: results produced** | 14 | 14 | 13 | 12 |
| EQ001 Equivalent | 1 | 1 | 1 | 1 |
| EQ002 Divergent | 4 | 4 | 4 | 4 |
| EQ006 runtime-changed | 9 | 9 | 8 | 7 |
| pairs that kept their earlier result | 19 | 19 | 21 | 21 |
| pairs the pass failed on (P2-078) | 1 | 1 | 1 | 1 |

Every Unknown the budget pass produced replaced a `timeout`, and so did its Divergents, except that in each of
runs A, C and D one Divergent replaced an `abstraction` Unknown and in run B two replaced an `unaligned-loop` Unknown.

## The whole run's results

| | quick | A as it is | B `bound` 3 | C `resourceLimit` 5,000,000 | D IL at first pass's values |
|---|---|---|---|---|---|
| EQ001 Equivalent | 12,727 | 12,728 | 12,728 | 12,728 | 12,728 |
| EQ002 Divergent | 30 | 35 | 35 | 34 | 35 |
| EQ006 runtime-changed | 241 | 262 | 264 | 254 | 260 |
| EQ003 Unknown | 562 | 535 | 533 | 544 | 537 |
| EQ004 | 15 | 15 | 15 | 15 | 15 |
| EQ005 | 167 | 167 | 167 | 167 | 167 |
| Unknown `opaque` | 259 | 263 | 261 | 255 | 264 |
| Unknown `abstraction` | 166 | 193 | 210 | 173 | 193 |
| Unknown `timeout` | 106 | 34 | 28 | 80 | 35 |
| Unknown `unaligned-loop` | 11 | 25 | 14 | 16 | 25 |
| Unknown `unmatched-overload` | 19 | 19 | 19 | 19 | 19 |
| Unknown `recursion` | 1 | 1 | 1 | 1 | 1 |
| `queryEndings.resourceLimit` | 177 | 204 | 186 | 188 | 201 |
| `queryEndings.wallClock` | 0 | 4 | 1 | 0 | 5 |

- No result quick decides has another rule id in any thorough run.
- Every `timeout` Unknown of a thorough run carries `failureRefinement`. Both answers are `unknown` on 33 of 34 in
  run A, 27 of 28 in run B, 78 of 80 in run C and 34 of 35 in run D; one pair in each has both `none-proved`, and
  one in run C has both `found`. Quick's 106 carry none.
- The pass sizes are smaller than ticket P1-032 measured (255 and 38 pairs), and quick is faster (368 s against
  999 s) with fewer `timeout` Unknowns (106 against 183): P1-030, P1-031 and P1-038 landed in between.

## Knob by knob

**`bound`, 8 against 3 (run A against run B).** The budget pass takes 22,982 s at 8 and 9,113 s at 3: the bound
of 8 is 60% of the pass. The median pair takes 2.4 times as long and the slowest tenth five times as long. For
that the pass produced 72 results at 8 and 79 at 3, and 13 Divergents against 15.
- Twelve pairs are Divergent in both.
- Three are Divergent only at 3 (`GitUI.GitUICommands::InitializeArguments`,
  `ResourceManager.Xliff.TranslationUtil::GetTranslatableTypes()`,
  `ICSharpCode.TextEditor.Actions.ToggleLineComment::ShouldComment`). In each, rung 1 reports a divergence within
  three iterations at `bound` 3; at `bound` 8 the same rung runs out of 30,000,000 on the larger unrolling.
- One is Divergent only at 8 (`GitCommands.Git.GetAllChangedFilesOutputParser::GetAllChangedFilesFromString_v2(string)`),
  and not because of depth. Its first result is an `abstraction` Unknown from rung 1. At `bound` 8 rung 1 times out
  instead, the ladder goes on, and rung 2's base obligation finds the counterexample. Run C finds it the same way.
- So no Divergent of run A needed more than three iterations to show. The bound of 8 found no divergence
  that lies between the fourth and the eighth iteration of a loop on this pair.
- Of the Unknowns that left `timeout`, 59 did at 8 and 64 at 3; 34 and 28 `timeout` Unknowns are left.
- Of the 168 pairs, 41 are in the pass only because they have a loop or a self-call, with no step that hit a
  budget. At `bound` 3 they are asked what the first pass asked, and cost 88 s of pair time between them. At
  `bound` 8 they cost 6,691 s and one of them is the Divergent above.
- P1-032's first thorough run did not end because one pair's unrolling at `bound` 8 never finished; the unroll
  limit it added is still the only thing between that bound and such a pair.

**`resourceLimit`, 30,000,000 against 5,000,000 (run A against run C).** The budget pass takes 22,982 s at
30,000,000 and 1,994 s at 5,000,000, so the larger limit is 91% of the pass. It buys 13 Divergents against 5, and 59
Unknowns with a cause against 21; 34 `timeout` Unknowns are left against 80. Of the results that differ between the
two runs, 46 are a `timeout` at 5,000,000 that has an answer at 30,000,000 (8 Divergent, 38 an Unknown with a
cause), and none is the reverse. In run A the pass spends 1,768 s for each Divergent it reports, and 319 s for
each result it produces; in run B, 608 s and 115 s.

**`timeoutMs`, 600,000.** Not varied. It is the backstop behind the resource limit: it ended 4, 1, 0 and 5 queries
of the four runs, against 204, 186, 188 and 201 the resource limit ended. With four threads each query has four
times the value on the clock (`PairWorkers.Sharing`).

**The IL pass's values.**

| the IL pass verifies at | from | seconds | produced | EQ001 | EQ002 | EQ006 |
|---|---|---|---|---|---|---|
| 8 / 30,000,000 / 600,000 | run A | 2,163 | 14 | 1 | 4 | 9 |
| 3 / 30,000,000 / 600,000 | run B | 1,072 | 14 | 1 | 4 | 9 |
| 8 / 5,000,000 / 600,000 | run C | 191 | 13 | 1 | 4 | 8 |
| 3 / 2,000,000 / 60,000 | run D | 28 | 12 | 1 | 4 | 7 |

The proof and the four EQ002 come at every value. The larger resource limit adds two EQ006:
`GitUI.UserEnvironmentInformation::GetInformation()` needs more than 5,000,000, and
`GitCommands.GitModule::GetInteractiveRebasePatchFiles()` more than 2,000,000. The bound of 8 adds nothing and
doubles the pass. At 3 / 30,000,000 the two extra EQ006 cost 1,044 s over the first pass's values, 522 s each, which
is about what the budget pass pays for a Divergent at the same values (608 s). Whether the IL pass's Divergents
are real is P2-135's question, not this report's.

**Repeatability.** Runs A and D have the same budget pass. Three of its 168 pairs ended differently
(`GitCommandsTests.GitModuleTests::ParseGitBlame()`, `ICSharpCode.TextEditor.Document.HighlightColor::.ctor`,
`ICSharpCode.TextEditor.Document.TextUtilities::GetExpressionBeforeOffset`), each a `timeout` in one run and an
Unknown with a cause in the other; no Divergent differs. That is the variation P2-100 describes, and it is the
size of noise the comparisons above are read against: the Divergent counts of runs A and B differ by more pairs
than that, and each of those four pairs has its cause in its ladder.

## What was decided
- ADR 0049's budget pass verifies at `bound` 3, `resourceLimit` 30,000,000, `timeoutMs` 600,000 (a clarification
  of ADR 0049, dated 2026-10-08). The `bound` of 8 cost 13,869 s of a 22,982 s pass and decided nothing the bound
  of 3 does not; three Divergents are lost to it.
- `resourceLimit` 30,000,000 stays. It is 91% of the pass's time and where 8 of its 13 Divergents and 38 of its 59
  better Unknowns come from. ADR 0049 chose it for the Divergents, and decision 7 says a low yield places work in
  thorough and does not remove it.
- `timeoutMs` 600,000 stays: it was not varied, and it ends few queries.
- The IL pass keeps the budget pass's values, now 3 / 30,000,000 / 600,000: every result the pass produced in any
  run, at half the time of the values it had.
- Thorough as it is after this ticket is run B: 10,870 s on this pair with four threads, 30 times quick's 368 s.
  For that it proves 1 more pair Equivalent (the IL pass), reports 28 more Divergent (5 EQ002 and 23 EQ006; 15
  from the budget pass and 13 from the IL pass) and leaves 29 fewer Unknown, 78 fewer of them `timeout`.
- Not decided here: more than four threads (P2-132), whether thorough is the default (ADR 0052), and whether
  the IL pass's Divergents are real (P2-135).
