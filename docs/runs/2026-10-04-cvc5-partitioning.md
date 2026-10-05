# P1-036 cvc5 partitioning spike: gitextensions-8522 (2026-10-04)

Question: of the 34 rung 1 queries cvc5 times out on, how many does it decide when it splits each
into partitions and every partition is solved by a process of its own?

**Answer: of the 34, partitioning decides 9; none is a proof (no query is unsatisfiable, so no pair
would be Equivalent); none replays to a Divergent.** All nine are satisfiable. Three replay to
Unknown(abstraction) and six cannot be read back, because Z3 cannot complete the model. Four of the
34 got a different answer the second time the same run was made. On the control, 10 of the 59 files
cvc5 answers alone are no longer answered in `timeoutMs` at 8 partitions.

## Setup
- Input: the files P1-025 exported for `gitextensions-8522` and its `results.tsv`
  (`docs/runs/2026-10-04-solver-portfolio.md`): 142 rung 1 queries Z3 gives up on, each with every
  `seq.++` of one argument unwrapped. cvc5 alone times out on 34 of them and answers 59.
- Solver: cvc5 1.4.1 (git 2b2e844), the binary P1-025 fetched, `cvc5.exe` SHA-256
  `04ee0af071fc18796ce9bf809fceac36e23893f8c2e42c829c4461bc9f0bec91`, unpacked under `.corpus/` and
  not committed.
- Tool: `tools/spikes/cvc5-partitioning/` (about 800 lines). `--self-test` passes: on a satisfiable
  and an unsatisfiable sample the partitioned answer equals the plain one.
- equiv: `main` at `ab66987` for the read-back, default config (`timeoutMs` 60,000). All 86
  answers read back were read back against a pair that still encodes to the file P1-025 exported.
- Machine: one Windows box, 24 logical cores, at most 24 cvc5 processes at once: three queries
  side by side at 8 partitions, one at 24. Other work ran beside it. The 816 query runs took 5.1
  hours.

## Method
**Splitting.** cvc5 is run once on the file with

```
--arrays-exp --compute-partitions=<8|24> --partition-strategy=<strategy> --partition-when=climit
--checks-before-partition=1 --checks-between-partitions=1 --write-partitions-to=<file>
```

for at most `timeoutMs`. The trigger is the check count, at the first check: the time trigger does
not repeat, and the paper's default start of 30 seconds would leave a partition half of the budget.
The release offers six strategies and all six were run. The spike's README has what the installed
`--help` says of each option.

**Solving.** A partition is the query with one more assertion. Each is run by its own cvc5 process
with `--arrays-exp`, all of a query's partitions at once, each for `timeoutMs` of wall-clock time.
The query is satisfiable when one partition is (the others are stopped), and unsatisfiable when
every partition is and the partitions cover every case. The cover is checked by one more process:
the file's declarations and the negation of the partitions' disjunction must be unsatisfiable. All
372 partition sets passed it.

**Read-back.** As P1-025: the satisfiable partition's values for the Bool and bit-vector constants
are asserted beside the query, Z3 completes the model, and `ModelDecoder.Replay` decides.

**What cvc5 does that its help does not say.**
- Asked for 24 partitions, a cube strategy writes 16: 2 to the power of floor(log2 24).
- After writing partitions cvc5 prints `unsat` for the query, whatever the query is. The splitting
  run's own `unsat` is therefore never taken.
- Partitions are written at a theory check of the main SAT solver. A query settled before that has
  none.

**A first run was discarded.** The spike's first driver did not stop every process at `timeoutMs`
(some ran four minutes). It was stopped after 276 query runs, and none of its numbers are here. In
the run reported, no process outlived its limit by more than five seconds.

## The 34 files cvc5 timed out on (criterion 2)
"Replayed" is a read-back that ends Divergent or Unknown(abstraction). "Not split" is a query cvc5
wrote no partition for in `timeoutMs`. Wall-clock is the sum over the 34 queries of the time from
the start of splitting to the query's answer; process seconds is the processor time of every cvc5
process.

Run 1.

| partitions asked | strategy | queries split | partitions made, median | unsatisfiable | satisfiable, replayed | satisfiable, not replayed | still undecided | not split | error | wall-clock s | process s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 8 | decision-scatter | 23 | 8 | 0 | 2 | 1 | 20 | 11 | 0 | 2285 | 5760 |
| 8 | heap-scatter | 0 | n/a | 0 | 0 | 0 | 0 | 34 | 0 | 2042 | 1988 |
| 8 | lemma-scatter | 22 | 8 | 0 | 0 | 1 | 21 | 12 | 0 | 2417 | 2358 |
| 8 | decision-cube | 24 | 8 | 0 | 3 | 0 | 21 | 10 | 0 | 2318 | 8451 |
| 8 | heap-cube | 0 | n/a | 0 | 0 | 0 | 0 | 34 | 0 | 2042 | 1966 |
| 8 | lemma-cube | 24 | 8 | 0 | 0 | 1 | 23 | 10 | 0 | 2416 | 2460 |
| 24 | decision-scatter | 24 | 24 | 0 | 2 | 5 | 17 | 10 | 0 | 2208 | 11676 |
| 24 | heap-scatter | 0 | n/a | 0 | 0 | 0 | 0 | 34 | 0 | 2041 | 1961 |
| 24 | lemma-scatter | 24 | 24 | 0 | 0 | 0 | 24 | 10 | 0 | 2460 | 2385 |
| 24 | decision-cube | 24 | 16 | 0 | 3 | 2 | 19 | 10 | 0 | 2186 | 12469 |
| 24 | heap-cube | 0 | n/a | 0 | 0 | 0 | 0 | 34 | 0 | 2041 | 1846 |
| 24 | lemma-cube | 24 | 16 | 0 | 0 | 1 | 23 | 10 | 0 | 2335 | 2524 |

Run 2.

| partitions asked | strategy | queries split | partitions made, median | unsatisfiable | satisfiable, replayed | satisfiable, not replayed | still undecided | not split | error | wall-clock s | process s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 8 | decision-scatter | 24 | 8 | 0 | 2 | 2 | 20 | 10 | 0 | 2253 | 5961 |
| 8 | heap-scatter | 0 | n/a | 0 | 0 | 0 | 0 | 34 | 0 | 2041 | 2007 |
| 8 | lemma-scatter | 23 | 8 | 0 | 0 | 0 | 23 | 11 | 0 | 2458 | 2434 |
| 8 | decision-cube | 23 | 8 | 0 | 3 | 0 | 20 | 11 | 0 | 2281 | 8128 |
| 8 | heap-cube | 0 | n/a | 0 | 0 | 0 | 0 | 34 | 0 | 2042 | 2000 |
| 8 | lemma-cube | 24 | 8 | 0 | 0 | 0 | 24 | 10 | 0 | 2414 | 2444 |
| 24 | decision-scatter | 22 | 24 | 0 | 2 | 4 | 16 | 12 | 0 | 2388 | 8840 |
| 24 | heap-scatter | 0 | n/a | 0 | 0 | 0 | 0 | 34 | 0 | 2042 | 1933 |
| 24 | lemma-scatter | 23 | 24 | 0 | 0 | 0 | 23 | 11 | 0 | 2497 | 2222 |
| 24 | decision-cube | 22 | 16 | 0 | 2 | 1 | 19 | 12 | 0 | 2414 | 8335 |
| 24 | heap-cube | 0 | n/a | 0 | 0 | 0 | 0 | 34 | 0 | 2049 | 1294 |
| 24 | lemma-cube | 22 | 16 | 0 | 0 | 0 | 22 | 12 | 0 | 2498 | 2130 |

For comparison, cvc5 alone spends 34 x 60 = 2,040 s of wall-clock and of one core on these files
and decides none.

Over every run, by partition:

| a partition's answer | partitions |
|---|---|
| unsatisfiable | 3596 |
| timeout | 1159 |
| satisfiable | 38 |
| stopped, because another partition was satisfiable | 171 |

- The two heap strategies split nothing: in 60 s they write no partition for any of the 34.
- Ten files are never split by any strategy in any run. They are 6 MB to 110 MB of text, and cvc5
  does not reach its first check on them in 60 s. Three more are split in some runs and not others.
- 24 of the 34 queries end, at some count and strategy, with every partition but one refuted. What
  makes the query hard stays in one partition.
- A decided query took 0.7 s to 101 s of wall-clock from the start of splitting; two of the nine
  take under 10 s.

## The same run, twice (criterion 3)

| partitions asked | strategy | queries | a different answer the second time |
|---|---|---|---|
| 8 | decision-scatter | 34 | 1 |
| 8 | heap-scatter | 34 | 0 |
| 8 | lemma-scatter | 34 | 1 |
| 8 | decision-cube | 34 | 0 |
| 8 | heap-cube | 34 | 0 |
| 8 | lemma-cube | 34 | 1 |
| 24 | decision-scatter | 34 | 1 |
| 24 | heap-scatter | 34 | 0 |
| 24 | lemma-scatter | 34 | 0 |
| 24 | decision-cube | 34 | 2 |
| 24 | heap-cube | 34 | 0 |
| 24 | lemma-cube | 34 | 1 |

**Queries with a different answer the second time, at any count and strategy: 4 of 34.** Every
difference is satisfiable in one run and undecided in the other; none is satisfiable against
unsatisfiable. In each, the run that answered did so 42 s to 101 s after splitting began, so the
wall-clock limit on a loaded box decides which run gets the answer. Nothing here bounds a partition
by a resource limit, as ADR 0050 decision 5 asks of cvc5.

## Decided queries (criteria 2 and 5)
Each cell of "by" is run, partitions asked, strategy.

| procedure identity | query | answer | by | worth |
|---|---|---|---|---|
| `GitUI.RevisionGridControl::goToMergeBaseCommitToolStripMenuItem_Click(object,System.EventArgs)` | `divergence` | sat | 1, 8, decision-cube; 1, 8, decision-scatter; 2, 8, decision-scatter; 2, 8, decision-cube; 1, 24, decision-scatter; 1, 24, decision-cube; 2, 24, decision-scatter; 2, 24, decision-cube | Unknown(abstraction) |
| `GitUI.CommandsDialogs.RevisionDiffControl::saveAsToolStripMenuItem1_Click(object,System.EventArgs)` | `divergence` | sat | 1, 8, decision-cube; 1, 8, decision-scatter; 2, 8, decision-scatter; 2, 8, decision-cube; 1, 24, decision-scatter; 1, 24, decision-cube; 2, 24, decision-scatter | Unknown(abstraction) |
| `GitUI.Editor.FileViewer::ResetView(GitUI.Editor.FileViewer.ViewMode,string,GitUI.UserControls.FileStatusItem,string)` | `divergence` | sat | 1, 8, decision-cube; 2, 8, decision-cube; 1, 24, decision-cube; 2, 24, decision-cube | Unknown(abstraction) |
| the same | `divergence` | sat | 1, 8, decision-scatter; 2, 8, decision-scatter; 1, 24, decision-scatter; 2, 24, decision-scatter | Z3 cannot complete the model |
| `GitUI.CommandsDialogs.RevisionDiffControl::DeleteSelectedFiles()` | `divergence` | sat | 1, 8, lemma-cube; 1, 24, decision-scatter; 1, 24, lemma-cube | Z3 cannot complete the model |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.AppearanceSettingsPage::SettingsToPage()` | `divergence` | sat | 2, 8, decision-scatter; 1, 24, decision-cube | Z3 cannot complete the model |
| `GitUI.CommandsDialogs.SettingsDialog.Pages.FormFixHome::LoadSettings()` | `divergence` | sat | 1, 8, lemma-scatter | Z3 cannot complete the model |
| `GitUI.Editor.Diff.DiffLineNumAnalyzer::Analyze(string)` | `divergence` | sat | 1, 24, decision-scatter; 1, 24, decision-cube; 2, 24, decision-scatter; 2, 24, decision-cube | Z3 cannot complete the model |
| `GitCommands.Executable.ProcessWrapper::.ctor(string,string,string,bool,bool,bool,System.Text.Encoding,bool)` | `divergence` | sat | 1, 24, decision-scatter; 2, 24, decision-scatter | Z3 cannot complete the model |
| `GitCommands.GitModule::GetInteractiveRebasePatchFiles()` | `divergence` | sat | 1, 24, decision-scatter; 2, 24, decision-scatter | Z3 cannot complete the model |

- Nine queries: three replay to Unknown(abstraction), six are not read back. One of the three
  replays from the cube strategies' model and is not read back from the scatter strategies'.
- No query is unsatisfiable, so there was no later rung 1 query to count: 0 proofs.
- The six not read back are the limit P1-025 named: Z3 is asked the same hard query with some
  constants fixed, and gives up again.
- The most any one count and strategy decides is 7 (24, decision-scatter, run 1); 6 in run 2.

## Control: the 59 files cvc5 already answers (criterion 4)
At 8 partitions with `decision-scatter`, which decided the most of the 34 at 8 partitions over the
two runs (7 answers against `decision-cube`'s 6).

| partitions asked | strategy | files | same answer | the other answer | no longer answered | of those: not split | wall-clock s | process s | plain cvc5 s (P1-025) |
|---|---|---|---|---|---|---|---|---|---|
| 8 | decision-scatter | 59 | 49 | 0 | 10 | 3 | 1290 | 1954 | 646 |

**It no longer answers 10 of the 59 in `timeoutMs`.** No file gets the other answer.
- Seven are split and left undecided: a partition times out. Plain cvc5 took 25 s to 58 s on each.
- Two are not split in 60 s; plain cvc5 took 35 s on each.
- One cvc5 answers `unsat` in the splitting run before it writes a partition, as it does alone in
  150 ms. The spike does not take that answer (see Method); a product that told the two cases apart
  could.
- Both unsatisfiable files that were split are unsatisfiable in every partition.
- The 47 satisfiable answers read back as P1-025's did but for four: two Z3 could not complete
  then replay to a Divergent now, and two that replayed then (one Divergent, one
  Unknown(abstraction)) Z3 cannot complete now. Another partition gives another model.

## What the answers are worth
- Partitioning moves 3 of 34 queries from a timeout to Unknown(abstraction), and finds a model for
  6 more that today's read-back cannot use. It proves nothing and refutes nothing.
- It is the paper's expectation and less: the gain is on satisfiable queries only, and the hard
  part of a query does not divide.
- It costs cores P2-077 also wants: 8 to 24 for up to 60 s a query, against one.
- As a replacement for plain cvc5 it loses answers (10 of 59). It can only come after plain cvc5
  has given up, as a later step.
- Its answers do not repeat while the clock ends a partition.

## Decision
A query is decided and answers differ between runs, so criterion 6 asks for the ticket, blocked by
P2-100: P1-039. Under ADR 0049 decision 7 the yield places it in thorough mode only and after
plain cvc5; it does not veto it.
