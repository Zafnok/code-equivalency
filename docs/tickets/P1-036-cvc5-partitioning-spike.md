# P1-036 Spike: does cvc5 decide more of the hard queries when it partitions them?
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-025

## Goal
P1-025 gave cvc5 the 142 rung 1 queries Z3 gives up on for `gitextensions-8522`: it answers 59,
fails to read 49 and times out on 34 (`docs/runs/2026-10-04-solver-portfolio.md`). cvc5 can split a
query into partitions while it solves ("Partitioning Strategies for Distributed SMT Solving",
Wilson et al., FMCAD 2023, arXiv 2306.05854; options `--compute-partitions`,
`--partition-strategy`, `--partition-when`), and each partition is then solved by its own process.
The box has 24 cores and a rung 1 query uses one.

A full read of the paper says to expect little: it evaluates only on arithmetic and uninterpreted
functions (no bit-vectors, arrays, datatypes or sequences), with 20-minute budgets, and an
independent comparison at 8 threads shows a net gain of 2 of 239. That is a reason to measure
before building, not a reason to skip: any query decided is a gain, and ADR 0049
decision 7 gives a low-yield technique a place in thorough mode. This is a spike. It changes
nothing under `src/`.

## Spec references
ADR 0050, ADR 0049, ADR 0014, ADR 0026, `tools/spikes/solver-portfolio/` (the exported files, `External`, the
read-back), `docs/runs/2026-10-04-solver-portfolio.md`, `docs/runs/2026-10-01-timeout-budget.md`,
P2-100 (the same run gives the same results), P2-077 (cores are also wanted for pairs in parallel).

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/spikes/cvc5-partitioning/` takes P1-025's exported files and, for each of the 34 that
   cvc5 timed out on, asks cvc5 1.4.1 for 8 and for 24 partitions, with each partition strategy
   the release offers and the check-count trigger (the time trigger is not repeatable), then
   solves every partition in its own process for `timeoutMs`. Its README records the exact options
   and what the installed version's `--help` says they do.
2. A query is unsatisfiable when every partition is, and satisfiable when one is. A satisfiable
   answer is read back and replayed as P1-025's were. Per partition count and strategy: queries
   unsatisfiable, satisfiable and replayed, satisfiable and not replayed, still undecided, error;
   and the wall-clock and summed process seconds.
3. The same run is made twice. The report says how many queries got a different answer the second
   time.
4. A control: the 59 files cvc5 already answers, at 8 partitions with the best strategy of
   criterion 2. The report says how many it no longer answers in `timeoutMs`.
5. `docs/runs/<date>-cvc5-partitioning.md` holds the tables and one line: the number of the 34
   that partitioning decides, how many of those are proofs (the pair would be Equivalent, counting
   rung 1's later queries as P1-025 did), and how many replay to a Divergent. Identities and counts
   only.
6. If at least one query is decided and criterion 3 found no differing answer, write the ticket
   that adds partitioning to P1-033's cvc5 solver as ADR 0049 decision 7 places it: a row in
   thorough mode's table (P1-032), with a config key for the partition count, and not in quick. If a
   query is decided but answers differ between runs, write the ticket and name P2-100 as its
   blocker. Otherwise one measured line in ROADMAP's post-MVP list.

## Files
`tools/spikes/cvc5-partitioning/**`, `docs/runs/<date>-cvc5-partitioning.md`, and either a ticket
or `docs/ROADMAP.md`.

## Tests
None beyond the spike's own `--self-test`: on one sample query that is satisfiable and one that is
not, the partitioned answer equals the plain one.

## Size guard
Any edit under `src/`: stop. A third solver, or Z3's own `smt.threads`: stop, that is another spike.

## Out of scope
The 49 files cvc5 cannot read (P1-033's rewrites). The other two large runs' timeouts. Sharing
lemmas between partitions.

## Notes
- cvc5 is fetched as P1-025 did, under `.corpus/solvers/`, never committed (ADR 0017: LGPL parts).
- The paper's own headline results use a portfolio of strategies and a 30-second partition start
  time by default; with a 60-second budget the start trigger has to be set far lower. Record what
  was used.
- Requested 2026-10-04 after the paper review, which had ranked this a drop.
