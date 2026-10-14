# P1-039 cvc5 partitions in the product: gitextensions-8522 (2026-10-14)

Questions (ticket P1-039 criteria 5 and 6): does a query split into partitions by the product end the same way twice;
and what does `solvers.cvc5.partitions` do to `gitextensions-8522`'s `timeout` Unknowns in thorough mode, at what
cost? Which count, 8 or 24, does the product take?

**Answer: the split repeats. Both runs give the same answer on all 34 of P1-036's queries at 8 partitions and at 24,
and every query makes the same partitions with the same endings: 0 queries differ.** The resource limit, not the
clock, ends almost every partition (2 of 178 at 8 and 3 of 530 at 24 end on the clock, the same ones in both runs).
**In a thorough run of the pair the key changes no result**: 22 `timeout` Unknowns with it and without it, and every
result the same. It costs time: 9,503 s at 8 and 9,562 s at 24 against 6,026 s without, all of it in the budget
pass. The count chosen is **8**.

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`).
- equiv: branch `P1-039-cvc5-partitions`, Release, win-x64. The thorough run without the key ran at `4f81d309`, which
  splits nothing without it; every run that splits ran the code of `6c8d693a`, where a satisfiable partition stops the
  partitions written after it. Default config (bound 3, `resourceLimit` 2,000,000, `timeoutMs` 60,000; the budget pass
  30,000,000 and 600,000), with `"solvers": { "cvc5": { "path": ... } }` in every run and `"partitions"` as named.
  `--jobs` not given: 24 threads, so the budget pass gives a query 6 times `timeoutMs` on the clock (P2-132), and cvc5
  the same as its `--tlimit`.
- Solver: cvc5 1.4.1 (git 2b2e844), `cvc5.exe` SHA-256
  `04ee0af071fc18796ce9bf809fceac36e23893f8c2e42c829c4461bc9f0bec91`, P1-025's binary, `--rlimit` 2,000,000
  (`Cvc5Solver.DefaultResourceLimit`). Splitting: `decision-scatter` at the first check.
- Machine: one Windows box, 24 logical cores. Other work ran beside every run here: the criterion 5 harness and the
  thorough runs overlapped each other, and other sessions ran corpus runs. Wall-clock times below are on that load.
- The box shut down at 05:47 during the first thorough run at 24 partitions and the harness's second run at 24. That
  thorough run is void and was run again from the start. The harness's two queries in flight (80 and 88) were written
  as the processes were killed; those two rows were dropped and the two queries asked again. Nothing else was kept from
  before the shutdown that it touched.

## The 34 queries, twice (criterion 5)
Input: the 34 rung 1 queries cvc5 timed out on in P1-036, the indices its `partitioned.tsv` lists for `timeouts`. The
files are P1-025's export (`20261004-p1025-final3`, the folder P1-036 read; the same 34 files are byte-identical in
`-final`). Tool: `tools/spikes/cvc5-partitions/`, which hands each file to the product's `Cvc5Solver.Split` with a
wall-clock limit of 600 s (the budget pass's `timeoutMs`) and the product's resource limit, and records how every
partition ended. It does not ask the plain query again: P1-036 measured that cvc5 gives up on each, and the product
splits only a query it gave up on (criterion 3's tests). As many queries run side by side as 24 processes allow at the
count: three at 8, one at 24.

| run | partitions asked | queries | split | not split | sat | unsat | unknown | partitions made | unsat | sat | resource limit | wall-clock | stopped | other | query s, summed |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 8 | 34 | 23 | 11 | 7 | 0 | 27 | 178 | 103 | 8 | 59 | 2 | 6 | 0 | 6,836 |
| 2 | 8 | 34 | 23 | 11 | 7 | 0 | 27 | 178 | 103 | 8 | 59 | 2 | 6 | 0 | 5,991 |
| 1 | 24 | 34 | 23 | 11 | 9 | 0 | 25 | 530 | 367 | 12 | 125 | 3 | 23 | 0 | 7,044 |
| 2 | 24 | 34 | 23 | 11 | 9 | 0 | 25 | 530 | 367 | 12 | 125 | 3 | 23 | 0 | 7,403 |

"Not split" is a query cvc5 wrote no partition for in its limits. "Stopped" is a partition written after a satisfiable
one, which the product stops. "Wall-clock" is a partition cvc5 ended at its `--tlimit` or that was killed after it.

| partitions asked | queries in both runs | a different answer the second time | a different set of partitions or endings |
|---|---|---|---|
| 8 | 34 | **0** | 0 |
| 24 | 34 | **0** | 0 |

- The partitions the clock ended are the same ones in both runs: at 8, one partition each of queries 5 and 54; at 24,
  one each of 5, 54 and 80. Each ran its whole 600 s without reaching 2,000,000 steps. In query 5 that partition comes
  before the satisfiable one, so it is waited for and the query takes 600 s to answer.
- No query is unsatisfiable, so no cover check was asked.
- Against P1-036, which gave each partition 60 s and no resource limit: at 8 with `decision-scatter` it decided 3 and
  then 4 and differed on 1 query; here 7 both times. At 24 it decided 7 and then 6; here 9 both times.

The satisfiable queries:

| query | procedure identity | 8 | 24 |
|---|---|---|---|
| 5 | `GitUI.RevisionGridControl::goToMergeBaseCommitToolStripMenuItem_Click(object,System.EventArgs)` | sat | sat |
| 23 | `GitUI.CommandsDialogs.FormRemotes::SaveClick(object,System.EventArgs)` | | sat |
| 27 | `GitUI.CommandsDialogs.FormResolveConflicts::SaveAs(string)` | sat | sat |
| 29 | `GitUI.CommandsDialogs.RevisionDiffControl::saveAsToolStripMenuItem1_Click(object,System.EventArgs)` | sat | sat |
| 30 | `GitUI.CommandsDialogs.RevisionDiffControl::DeleteSelectedFiles()` | sat | sat |
| 35 | `GitUI.CommandsDialogs.SettingsDialog.Pages.AppearanceSettingsPage::SettingsToPage()` | sat | |
| 48 | `GitUI.Editor.FileViewer::ResetView(GitUI.Editor.FileViewer.ViewMode,string,GitUI.UserControls.FileStatusItem,string)` | sat | sat |
| 64 | `GitCommands.Executable.ProcessWrapper::.ctor(string,string,string,bool,bool,bool,System.Text.Encoding,bool)` | | sat |
| 100 | `TeamCityIntegration.TeamCityAdapter::Initialize(GitUIPluginInterfaces.BuildServerIntegration.IBuildServerWatcher,GitUIPluginInterfaces.ISettingsSource,System.Action,System.Func<global::GitUIPluginInterfaces.ObjectId, bool>)` | | sat |
| 123 | `GitCommandsTests.Git.SubmoduleHelpersTest::GetSubmoduleNamesFromDiffTest()` | sat | sat |

These are queries of P1-025's export of 2026-10-04. In the product of today none of them reaches a split: in the
thorough run below, 8 of the 10 pairs end another way (Equivalent by congruence, Unknown(abstraction) or EQ006).
The other 2, `SettingsToPage` and `GetSubmoduleNamesFromDiffTest`, are `timeout` Unknowns whose query plain cvc5 now
answers `sat` in seconds, so nothing is split, and Z3 cannot complete the model.

## gitextensions-8522 in thorough mode, with and without the key (criterion 6)

Three thorough runs of the pair, one after the other, each with cvc5 configured (`--mode thorough`, default config
otherwise, 24 threads). Without the key at `4f81d309`; with it at `6c8d693a`. The run at 24 is the second one; the
first was void (Setup).

| `solvers.cvc5.partitions` | Unknown(`timeout`) | EQ001 | EQ002 | EQ003 | EQ006 | results naming cvc5 | queries split | of those not sent | `sat` from partitions | kept | run s | budget pass s |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| not set | 22 | 12,795 | 33 | 599 | 184 | 6 | 0 | n/a | n/a | n/a | 6,026 | 4,414 |
| 8 | 22 | 12,795 | 33 | 599 | 184 | 6 | 33 | 14 | 1 | 0 | 9,503 | 7,998 |
| 24 | 22 | 12,795 | 33 | 599 | 184 | 6 | 33 | 14 | 1 | 0 | 9,562 | 7,955 |

**Every result is the same in the three runs**: rule, verdict, Unknown reason and `proofMethod`, pair by pair. No
`ladderTrace` step names partitions, since no answer from partitions was kept. `run.properties.queryEndings` is
`resourceLimit` 182, `wallClock` 0 in each (Z3's endings).

- "Queries split" counts the `<query>:cvc5-partitions` stages of the run log, over 30 results (the IL pass asks
  some again). 14 are queries that cannot be printed exactly and are not sent, as for plain cvc5 (P2-194 owns them).
  cvc5 split the other 19.
- The one `sat`, at both counts, is the `divergence` query of
  `GitCommands.GitModule::GetInteractiveRebasePatchFiles()`: plain cvc5 gave up in 8 s, a partition answered in 15 s, and Z3 could not complete the model from its values, the
  same loss P1-036 recorded for that query. Rung 1 stays a timeout, and the pair stays the Unknown(`unaligned-loop`)
  the budget pass gives it without the key.
- What the 22 `timeout` Unknowns are, with the key at 8:

  | what cvc5 did with the pair's rung 1 queries in the budget pass | results |
  |---|---|
  | split, and the partitions left it unknown | 8 |
  | plain cvc5 answered `sat`, and Z3 could not complete the model, so nothing was split | 7 |
  | the query cannot be printed exactly, so neither plain cvc5 nor a split is asked | 7 |

  The 7 in the middle are the largest share no technique here reaches. P1-043 reads cvc5's own model back.
- The time: the splits took 9,235 s at 8 and 9,057 s at 24 summed over the threads, and the run 3,477 s and 3,536 s
  more than without the key. Most of it is one pair: the `divergence` query of
  `GitExtensions.Plugins.GitImpact.ImpactControl::UpdatePathsAndLabels()`. Plain cvc5 runs it for 3,608 s to 3,614 s
  in the budget pass in all three runs, ended by the clock (600 s times 6 on 24 threads), never by its resource
  limit; its split takes another 3,600 s. P2-196 owns that cvc5 is ended by the clock there. The other splits took
  3 s to 2,063 s.
- The runs shared the box with parts of the criterion 5 tool's runs and with other sessions' work, and the load was
  not the same across the three, so the seconds are on load and differ by more than the key alone.

## Decision
**8.** In the product the two counts do the same: no result changes at either, and they cost the same time (9,503 s
and 9,562 s). On P1-036's queries 24 decides 2 more of 34, all satisfiable and none a query the product of today
splits, for 3 times the partitions (530 against 178) and the processes and cores they take. Both counts repeat.

Under ADR 0049 decision 7 the low yield keeps the technique in thorough mode, off unless the key is set, and does not
remove it: on `gitextensions-8522` it decides nothing today, and the answers it finds are lost to the read-back
(P1-043) or are on queries the clock ends (P2-196). VERIFICATION-MODEL.md section 6 and the README name 8 as the
measured count.
