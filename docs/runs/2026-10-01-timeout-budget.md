# P2-050 timeout budget: gitextensions-8522 (2026-10-01)

Question: of the pairs P2-046's run left Unknown(timeout), how many only need a larger solver budget,
and what should the default budget be once it is counted in Z3's own steps (`rlimit`) and not in
seconds?

**Answer: a larger budget decides no pair Equivalent. At 4x it turns 37 of 172 timeouts into
another answer, at 20x 73, and every one of them is a Divergent or an Unknown with a different
reason.** So the default keeps the size of today's budget and only changes its unit:
`resourceLimit` 5,000,000 with `timeoutMs` 60,000 behind it. At that setting the pairs give the
answers the 5,000 ms wall-clock budget gave (173 and 174 timeouts in two runs, against 172), and the
resource limit, not the clock, ends about 95% of the queries that give up.

**Two runs at the new default are not yet identical.** 179 of 184 pairs gave the same outcome, ladder
and detail twice. Four of the five that differ ran into the wall-clock backstop in one run. The
fifth exhausted the same resource limit in one run and found a model in the other, which a
deterministic budget alone should not allow. The evidence points at the timing of the .NET garbage
collector: with collections made rare, the same pairs gave the same outcome three times out of three
(see "Repeatability"). That is P2-082.

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`).
- Input: the 206 results with `unknownReason: timeout` in P2-046's `full` run (equiv bd8e379,
  `docs/runs/2026-09-30-full-gitextensions-8522/SUMMARY.md`). All 206 identities still match a pair.
- What was run: only those pairs. `compare` has no option to verify a subset, and no `--timeout-ms`,
  so a throwaway harness (not committed) loaded both solutions through the production frontend and
  handed each pair's two lowered bodies to `Z3Backend.Verify`, the call `compare` makes for a pair
  the solver has to decide, with the default bound and the budget under test. Nothing after the
  backend ran (no contracts pass, no `--execute`).
- equiv: ef79ff6 (`main`) for the wall-clock runs; this ticket's branch for the resource-limit runs.
- Machine: one Windows box, 24 logical cores. The runs overlapped: 4 to 8 solver threads per run and
  up to 20 at once. A wall-clock budget buys less under that load, equally at 1x, 4x and 20x; a
  resource limit buys the same.
- "sum of pair seconds" is the time the pairs took added up, which is what one thread would need.
  "wall-clock" is what the run took with its threads.

## Nine pairs take their time outside any budget
At 1x the 206 pairs took 24,731 pair seconds, and nine pairs took 22,750 of them. Each of the nine
is Unknown(timeout) at 1x after far longer than its budget allows, and eight still are at 20x
(`ImpactControl::UpdatePathsAndLabels()` becomes Unknown(opaque) after 5,817 s). That time is not
the solver using its budget, and neither limit ends it (P2-076 owns it).

| pair | seconds at 1x (5 s budget) | seconds at 20x | seconds at `resourceLimit` 10,000,000 |
|---|---|---|---|
| `GitUI.CommandsDialogs.FormRemotes::InitializeComponent()` | 11,414 | 15,661 | 10,549 |
| `ICSharpCode.TextEditor.TextView::PaintLinePart(System.Drawing.Graphics,int,int,int,System.Drawing.Rectangle,int)` | 6,210 | 8,696 | 5,100 |
| `GitUI.CommandsDialogs.FormBrowse::.ctor(GitUI.GitUICommands,string,GitUIPluginInterfaces.ObjectId,GitUIPluginInterfaces.ObjectId)` | 3,133 | 7,415 | 2,574 |
| `ICSharpCode.TextEditor.Document.DefaultHighlightingStrategy::ParseLine(ICSharpCode.TextEditor.Document.IDocument)` | 758 | 3,301 | 827 |
| `GitUI.CommandsDialogs.FormPush::PushChanges(System.Windows.Forms.IWin32Window)` | 451 | 740 | 1,275 |
| `GitUI.CommandsDialogs.BrowseDialog.DashboardControl.UserRepositoriesList::InitializeComponent()` | 323 | 770 | 757 |
| `GitExtensions.Plugins.GitImpact.ImpactControl::UpdatePathsAndLabels()` | 203 | 5,817 | 244 |
| `GitUI.CommandsDialogs.FormBrowse::InternalInitialize(bool)` | 131 | 269 | 184 |
| `GitUI.FileStatusList::UpdateFileStatusListView(bool)` | 127 | 528 | 95 |

The budget comparison below is over 184 pairs. The 22 left out are seven of these nine, known when
the 4x run started, and 15 pairs the 1x run had not reached by then (two of them the other two of
the nine). Over all 206 the picture is the same:

| budget | pairs | Equivalent | Divergent | still timeout | other Unknown | sum of pair seconds | wall-clock |
|---|---|---|---|---|---|---|---|
| 1x | 206 | 0 | 1 | 194 | 11 (abstraction 4, unaligned-loop 4, opaque 3) | 24,731 | 11,415 s on four threads |
| 4x | 206 | 0 | 17 | 153 | 36 (abstraction 19, unaligned-loop 10, opaque 7) | 27,555 | 10,083 s on four threads |
| 20x | 206 | 0 | 21 | 117 | 68 (abstraction 39, unaligned-loop 18, opaque 11) | 80,563 | 16,558 s on eight threads |

## Larger wall-clock budgets (criterion 1)

| budget | pairs | Equivalent | Divergent | still timeout | other Unknown | sum of pair seconds | wall-clock |
|---|---|---|---|---|---|---|---|
| 1x (5,000 ms) | 184 | 0 | 1 | 172 | 11 (abstraction 4, unaligned-loop 4, opaque 3) | 1,881 | part of the 206-pair run |
| 4x (20,000 ms) | 184 | 0 | 16 | 135 | 33 (abstraction 16, unaligned-loop 11, opaque 6) | 5,528 | 1,452 s on four threads |
| 20x (100,000 ms) | 184 | 0 | 21 | 99 | 64 (abstraction 36, unaligned-loop 18, opaque 10) | 35,209 | part of the 206-pair run |

- 12 of the 184 were no longer timeouts at 1x: `main` has moved since P2-046's run, and that run
  shared its machine with three others.
- Of the 172 that time out at 1x, 37 are decided at 4x (14 Divergent, 23 other Unknown) and 73 at
  20x (20 Divergent, 53 other Unknown). 99 still time out at 20x: 79 on rung 1's first query, 20 on
  an induction obligation.
- At 20x, 60 pairs are decided on rung 1, and every one by a model: the first query was satisfiable
  and the solver needed longer to find the input. 36 of the models depend on an abstraction, 6 reach
  an opaque node and 18 replay to a divergence. No first query came back unsatisfiable, so no pair
  was proved.
- Cost: 4x takes 2.9 times the solver time of 1x on these pairs and 20x takes 18.7 times.
- The answers are not stable even here. One pair is Divergent at 4x and Unknown(abstraction) at
  20x, and one is Unknown(abstraction) at 1x and Divergent at 4x.

## The resource limit

Z3 counts `rlimit` in units of its own work, and a unit's cost in time varies a great deal from
query to query. On the unit-test fixture `hard-multiplication` 10,000,000 units take 1.7 s. On these
pairs the same limit takes a median 13.8 s and up to 606 s. Rung 4's fixtures prove `loops/fusion`
and `loops/nesting-changed` with between 1,000,000 and 3,000,000 units in about 2 s.

Same 184 pairs. "Single-query seconds" is over the pairs whose ladder is one timed-out query, so it
is the time one query takes to exhaust the limit.

| `resourceLimit` | backstop | Divergent | timeout | other Unknown | sum of pair seconds | single-query seconds: median / p90 / p95 / max | timeouts ended by the backstop |
|---|---|---|---|---|---|---|---|
| 2,000,000 | 600 s | 0 | 178 | 6 | 3,001 | 4.3 / 22.9 / 50.9 / 119.6 | 0 |
| 5,000,000 (first run) | 60 s | 1 | 173 | 10 | 4,438 | 7.1 / 50.9 / 61.2 / 113.3 | 10 of 173 |
| 5,000,000 (second run) | 60 s | 1 | 174 | 9 | 4,377 | 6.1 / 47.1 / 60.0 / 104.2 | 8 of 174 |
| 10,000,000 | 600 s | 3 | 163 | 18 | 12,549 | 13.8 / 101.8 / 203.4 / 606.2 | 1 of 163 |
| 30,000,000 | 600 s | 13 | 136 | 35 | 28,459 | 32.9 / 272.0 / 501.8 / 707.8 | 4 of 136 |
| 1x wall-clock, for comparison | | 1 | 172 | 11 | 1,881 | 5.3 / 7.4 / 10.1 / 83.6 | all |

- 2,000,000 decides 6 pairs fewer than today's budget, 5,000,000 one or two fewer, 10,000,000
  nine more and 30,000,000 36 more, about what 4x decides, at 15 times the solver time of 1x. None of
  them decides a pair Equivalent.
- A resource limit costs more time than the wall-clock budget of the same strength: 2.3 times at
  5,000,000. A minority of queries spend a long time on each unit, and a wall-clock budget cuts
  exactly those short.
- The backstop has to sit well above the usual time, or it ends those slow queries and the result
  depends on the machine again. At 5,000,000 a 60 s backstop ends about 5% of the timeouts (10 of 173,
  then 8 of 174).

## Repeatability

Two runs of the 184 pairs at `resourceLimit` 5,000,000 and `timeoutMs` 60,000, in one process on
four threads:
- 179 pairs: the same outcome, ladder and detail in both.
- 4 pairs: timeout in both, on the resource limit in one run and on the backstop in the other.
- 1 pair: Unknown(abstraction) in one run, and timeout on the resource limit in the other.

For comparison, the wall-clock budget repeats far worse. The 4x budget was run twice on the same 184
pairs, in two processes under different load, and 14 pairs ended with a different outcome. At the
resource limit one did.

The same query took very different times to exhaust the same limit in the two runs (4.7 s and
36.1 s, 9.0 s and 45.8 s), so the solver did not do the same work. Checked on 35 pairs (those five,
the five with the largest time ratio, and 25 others), three times each on one thread:

| garbage collector | pairs identical in all three runs | pairs whose times differ by more than 1.5 times |
|---|---|---|
| default settings | 34 of 35 (the same pair flips again: abstraction once, timeout twice) | 7, up to 5.6 times |
| generation 0 budget raised to 8 GB, so that collections are rare | 35 of 35 (that pair is abstraction three times) | first two runs: 4, none over 1.6 times; third run: 4 more, up to 6.6 times |

With rare collections every outcome repeats, and in the first two runs the pairs that had swung by
2.6 to 5.6 times are steady. In the third run four of them swing again, which fits a collection
having happened by then but does not prove it. So the evidence points at the moment of a collection
changing the work the solver does on the same query, and it is not conclusive. The likely
mechanism, not yet confirmed: Z3's .NET binding releases a
term when the garbage collector finalises its wrapper (it enables `Z3_enable_concurrent_dec_ref`),
Z3 gives a freed term's id to the next term it builds, and several of its procedures order terms by
id. The resource limit removes the dependence on machine speed and load. It does not remove this.
P2-082.

## Decisions
- `resourceLimit` defaults to 5,000,000. A larger budget proves nothing more, so the default keeps
  today's strength, and 5,000,000 is where the outcomes match the 5,000 ms budget. It leaves rung
  4's fixture proofs a margin of at least 1.7 times.
- `timeoutMs` defaults to 60,000, up from 5,000. It is now only the backstop. At 5,000 it would end
  most queries before the resource limit and nothing would be gained.
- Cost on this pair: about 2,500 more solver seconds for the timeout pairs than today, on a `full`
  run whose verify phase took 24,000 s or more.

## Findings
- No timeout becomes Equivalent at 4x or 20x, and 99 of 172 still time out at 20x: P2-083.
- The same query and resource limit can end differently when the garbage collector runs at a
  different moment: P2-082.
- Nine pairs take 92% of the time, outside any budget: P2-076 (already open).
- ARCHITECTURE.md lists `--bound` and `--timeout-ms` for `compare`, and the command has neither.
  Only `equiv mcp`'s `compare` tool and `equiv.config.json` set them.
