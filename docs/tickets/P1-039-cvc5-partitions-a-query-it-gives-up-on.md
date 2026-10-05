# P1-039 In thorough mode, cvc5 splits a rung 1 query it gives up on into partitions solved a process each
Status: todo
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
