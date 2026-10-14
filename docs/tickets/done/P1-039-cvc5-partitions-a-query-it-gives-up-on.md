# P1-039 In thorough mode, cvc5 splits a rung 1 query it gives up on into partitions solved a process each
Status: done (PR #493)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-033, P1-032, P2-100

## Goal
P1-036 measured cvc5's partitioning on the 34 rung 1 queries of `gitextensions-8522` that cvc5
times out on (`docs/runs/2026-10-04-cvc5-partitioning.md`). It decides 9: all satisfiable, three
replay to Unknown(abstraction), six cannot be read back, none is a proof and none a Divergent. At
most 7 are decided by one count and strategy (24 partitions, `decision-scatter`). Used in place of
plain cvc5 it loses 10 of the 59 answers plain cvc5 gives. ADR 0049 decision 7 places a sound
technique with a low yield in thorough mode; it does not leave it out.

When done, a run in thorough mode with `solvers.cvc5.partitions` set asks cvc5 to split each rung 1
query that cvc5 has already given up on, and solves the partitions in processes of their own. A run
in quick mode, or without the key, behaves exactly as today.

**Blocked by P2-100.** In P1-036 four of the 34 queries got a different answer the second time the
same run was made. A baseline cannot live with that, and P2-100 is the ticket that makes one query
end one way. Do not start before it is done. The cause measured here is its own: a partition was
ended by the wall-clock, on a loaded box. Criterion 5 is what this ticket owes on top of P2-100.

## Spec references
ADR 0050 (decisions 3, 5 and 7), ADR 0049 (decisions 2, 5 and 7), ADR 0014, ADR 0026,
`docs/runs/2026-10-04-cvc5-partitioning.md`, `docs/runs/2026-10-04-cvc5-budget.md`,
`tools/spikes/cvc5-partitioning/` (`PartitionedSolve`, `Smt.Cover`: the shape, not the code to
keep; its README has the options and what cvc5 does that its help does not say),
`src/Equiv.Verify.Cvc5/Cvc5Solver.cs`, `Cvc5Process.cs`, `src/Equiv.Verify.Z3/SecondSolver.cs`,
VERIFICATION-MODEL.md section 6 (the mode table), P2-077 (the same cores).

## Design
- `equiv.config.json`: `solvers.cvc5.partitions`, an integer from 2 to 64. Absent, nothing is
  split.
- `Cvc5Solver.Ask` is unchanged. A second entry point takes a script cvc5 has answered `unknown`:
  it runs cvc5 once with `--compute-partitions=<n> --partition-strategy=decision-scatter
  --partition-when=climit --checks-before-partition=1 --checks-between-partitions=1
  --write-partitions-to=<file>`, reads the partitions (one a line, a repeated line once), and asks
  each partition, the script with one more assertion, in a process of its own with the same
  `--rlimit` and `--tlimit` a plain script gets.
- The answer is `sat` with that partition's values when one partition is `sat`; `unsat` when every
  partition is `unsat` and the cover check (the declarations and the negation of the partitions'
  disjunction) is `unsat`; otherwise the `unknown` it was. What happens to a `sat` or an `unsat`
  after that is P1-033's, unchanged: a `sat` is read back and replayed, never a verdict.
- The mode decides: thorough splits, quick never does. P1-032's options carry it.

Pitfalls.
- After it writes partitions, cvc5 prints `unsat` for the query whatever the query is. Never take
  the splitting run's own answer when a partition was written.
- A cube strategy writes each cube twice and 2^floor(log2 n) of them. `decision-scatter` writes n.
- Ten of the 34 files were never split in 60 s. No partition written is the `unknown` it was, and
  costs one more process for `timeoutMs`; the splitting run gets a `--tlimit` of its own.
- The partition processes use cores. With P2-077's `--jobs`, a pair's partitions count against the
  same number; do not start n processes a job.

## Acceptance criteria (all must hold; nothing beyond them)
1. `solvers.cvc5.partitions` is read; a value that is not an integer from 2 to 64 is a config
   diagnostic and splits nothing. Without the key every sample and existing test gives the result
   it gives today.
2. With a fake process runner: one `sat` partition gives `sat` with its values; every partition
   `unsat` and the cover `unsat` gives `unsat`; every partition `unsat` and the cover not `unsat`,
   a partition that is `unknown`, no partition written, and the splitting run's own `unsat` beside
   written partitions each leave `unknown`.
3. In thorough mode a rung 1 query is split only after Z3 and then plain cvc5 gave up on it. In
   quick mode no query is split. One test a mode names the step that did or did not run.
4. A result one of whose queries a partition answered has `proofMethod` suffixed `+cvc5` as today,
   and its `ladderTrace` step says how many partitions were asked for (snapshot test).
5. Repeatability. The 34 queries of P1-036 are run twice through the product at the chosen
   count. `docs/runs/<date>-cvc5-partitions.md` reports, per run, the answers and how many
   partitions the wall-clock ended, and the number of queries whose answer differs. If that number
   is not 0, stop and record it in Notes: the ticket is not done.
6. The same file reports `gitextensions-8522`'s `timeout` Unknowns in thorough mode with and
   without the key, and the wall-clock time of each run. The partition count is chosen there from
   8 and 24.
7. VERIFICATION-MODEL.md section 6 gains the row in the mode table (quick: no; thorough: yes, when
   `solvers.cvc5.partitions` is set) and the paragraph on cvc5 says what is split and when. README's
   config section names the key.

## Files
`src/Equiv.Verify.Cvc5/**`, `src/Equiv.Core/Configuration/{EquivConfig,EquivConfigLoader,EquivConfigDiagnosticIds}.cs`,
`src/Equiv.Core/` (`ISmtSolver` or the options, whichever carries the second entry point),
`src/Equiv.Verify.Z3/SecondSolver.cs`, `src/Equiv.Cli/**` (wiring), matching tests,
`docs/VERIFICATION-MODEL.md`, `README.md`, `docs/runs/<date>-cvc5-partitions.md`.

## Tests
Unit tests for criterion 2's cases, the partition file reader (a repeated line, an empty file),
the cover script, and the config key. The two mode tests of criterion 3. The snapshot of criterion 4.
One integration test, skipped when no cvc5 is configured, splits a sample query and gets the answer
plain cvc5 gives.

## Size guard
Sharing lemmas between partitions, a strategy other than `decision-scatter`, a portfolio of
strategies, or splitting a query for Z3: stop, each is another ticket. If criterion 5 cannot reach
0 with a resource limit ending the partitions: stop there.

## Out of scope
Splitting in quick mode. The 49 files cvc5 cannot read. Decoding cvc5's model without Z3 (P1-033's
Notes). Running Z3 and cvc5 at once.

## Notes
- From P1-036. The yield is 3 of 34 moved to Unknown(abstraction) and no verdict; it is scheduled
  because ADR 0049 decision 7 says a low yield places a technique and does not veto it.
- The six answers that could not be read back are P1-033's read-back limit, not this ticket's.
- Decision: the second entry point is `ISmtSolver.AskInPartitions(script, partitions, limit)`, and the count a pass asks
  for is `VerificationOptions.Partitions` (null splits nothing). `SecondSolver` calls it only when the plain answer was
  unknown, as the stage `<query>:cvc5-partitions`; `Passes.Of` sets it from `solvers.cvc5.partitions` in thorough mode
  only.
- Decision: in thorough mode the split goes with rung 5's local proposer and ADR 0037's queries: in the budget pass (and
  the IL pass, which takes its options), or in the first pass when there is no budget pass. A query that times out in
  the first pass hits a budget, so the budget pass reaches it; splitting it twice would cost cores for nothing.
- Decision: the splitting run gets the plain script's `--rlimit` as well as its own `--tlimit`, so whether partitions
  are written in its limits is decided by cvc5's step count, not by the clock (criterion 5).
- Decision: the first satisfiable partition in the order written gives the values. A satisfiable partition stops the
  partitions written after it (a running one is killed, one not started never starts) and every partition before it
  runs to its end, so which values come back does not depend on which process ended first. The first version waited
  for every partition: on query 5 of the 34 a satisfiable partition answered in a second and a later one held the
  query for the whole wall-clock limit, so the process runner gained a stop token.
- Decision: the processes of split queries share one count, the run's `jobs` (`Cvc5Solver.Processes`, a slot held only
  while a process runs). A pair whose query is split waits on its thread while its partitions run, so the pair's
  partitions take places from the same number as the pairs; a pair's own Z3 work is not counted against it.
- Decision: a `partitions` value outside 2 to 64 is a new diagnostic, CFG015, not CFG011: it is a value out of range,
  not a malformed `solvers` object. The path beside it still configures cvc5.
- Decision: the criterion 5 harness is `tools/spikes/cvc5-partitions/` (not in `Equiv.slnx`), which runs the product's
  `Cvc5Solver` and takes the test project's assembly name to read how each partition ended (`Cvc5Solver.Split`). It
  asks each query as the product does: plain first, split only if plain gave up.
- Decision: the 34 queries are the indices P1-036's own `partitioned.tsv` lists for the set `timeouts`. P1-025 left
  four output folders; P1-036 read `20261004-p1025-final3` (34 timeouts, 59 answered). The `results.tsv` of
  `20261004-p1025-final` lists 33 timeouts: there cvc5 could not read 69, 97 and 100 ("Cannot handle assertion"), and
  73 and 87 timed out. The 34 files are byte-identical in both folders, so the measurement asks exactly P1-036's queries.
- `tools/spikes/cvc5-budget/` implements `ISmtSolver`; it gains a pass-through `AskInPartitions` so it still builds.
- Decision: the harness gives each process 600 s, the budget pass's `timeoutMs`, where the product splits. On these 34
  queries cvc5's `--rlimit` 2,000,000 takes minutes: the first plain query ran its whole 600 s. So the harness does
  not ask the plain query again (P1-036 measured that cvc5 gives up on each), and runs queries side by side within
  24 processes.
- Criterion 5 (`docs/runs/2026-10-14-cvc5-partitions.md`): 0 of 34 queries differ between two runs, at 8 and at 24
  partitions, in answer and in every partition's ending. The clock ends 2 of 178 partitions at 8 and 3 of 530 at 24,
  the same ones in both runs. 7 sat at 8, 9 at 24, no unsat.
- Criterion 6: in thorough mode on `gitextensions-8522` the key changes no result at 8 or 24: 22 `timeout` Unknowns in
  each, every result equal. Runs: 6,026 s without, 9,503 s at 8, 9,562 s at 24, the difference all in the budget pass.
  Decision: the count is 8, the same yield there for a third of the partitions.
- The box shut down mid-way through the first thorough run at 24 and the harness's second run at 24. That run was
  discarded and run again; the harness's two rows written as its processes were killed were dropped and asked again.
- Finding, ticketed: plain cvc5 runs `ImpactControl::UpdatePathsAndLabels()`'s query to the clock (3,600 s on 24
  threads) in every run, never to its resource limit, and its split takes another 3,600 s: P2-196.
- Finding, ticketed: 7 of the 22 `timeout` Unknowns have a rung 1 query plain cvc5 answers `sat` whose model Z3 cannot
  complete, and the one `sat` from partitions is lost the same way: P1-043.
