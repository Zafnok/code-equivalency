# P1-036 cvc5 partitioning spike (throwaway)

Measures whether cvc5 decides more of the rung 1 queries it times out on when it splits each one
into partitions and every partition is solved by a process of its own. The result is in
`docs/runs/2026-10-04-cvc5-partitioning.md`.

It takes what the P1-025 spike (`tools/spikes/solver-portfolio/`) left: the exported files
(`NNN.u.smt2`, the query with each `seq.++` of one argument unwrapped) and `results.tsv`, which
says what cvc5 answered on each. It compiles that spike's `Rung1.cs` for the read-back.

- `Partitioned.cs`. `PartitionedSolve.Solve` runs cvc5 once on the file with the partitioning
  options below, reads the partitions it wrote (one a line; a line written twice counts once),
  writes one file a partition (the query with `(assert <partition>)` before its `check-sat`), and
  runs cvc5 on each, all at once, each for `timeoutMs` of wall-clock time. The query is
  - satisfiable when one partition is. The others are stopped, and that partition's `get-value`
    output is kept for the read-back;
  - unsatisfiable when every partition is and the partitions cover every case. The cover check is
    one more cvc5 process, on the file's declarations and `(assert (not (or <partition>...)))`,
    which must be unsatisfiable. A set cvc5 was stopped in the middle of writing fails it;
  - undecided otherwise, and "not split" when cvc5 wrote no partition in `timeoutMs`.

  After it writes partitions, cvc5 prints `unsat` for the query whatever the query is, so the
  splitting run's own answer is never taken as unsatisfiable. With no partition written, its
  `sat` is taken (the read-back checks it), and nothing else.
- `Program.cs`. `--solve` runs a set of files (`timeouts`: the 34 cvc5 timed out on; `answered`:
  the 59 it answers) at each count, strategy and run, and appends one line a query to
  `partitioned.tsv`; started again, it skips what is there. At most `--slots` processes run at
  once (24, the box's cores): a query's partitions take their places together, so three queries
  run side by side at 8 partitions and one at 24. A process that outlives its limit by more than
  five seconds is counted as `late` in the line. `--read-back` loads the pair through the
  production frontend and, for each satisfiable answer, runs P1-025's `ReadBack` (the values
  asserted beside the query, Z3 completes the model, `ModelDecoder.Replay` decides); for each
  unsatisfiable `divergence` query it asks Z3 rung 1's later queries, since only all of them prove
  the pair. `--report` prints the tables the run file quotes.
- `SelfTest.cs`. On one sample query that is satisfiable and one that is not (pigeons in holes,
  as equalities over an uninterpreted sort), the partitioned answer equals the plain one, at 8
  partitions with each strategy. A strategy that writes no partition before cvc5 answers is
  printed as "not split"; the two decision strategies must split.

It is not in `Equiv.slnx` and no CI gate builds it. It takes the assembly name
`Equiv.Tests.Integration` to use that project's `InternalsVisibleTo` grants, so `src/` is unchanged.

## The options, and what cvc5 1.4.1 says they do

The splitting run:

```
cvc5 --arrays-exp --compute-partitions=<8|24> --partition-strategy=<strategy>
     --partition-when=climit --checks-before-partition=1 --checks-between-partitions=1
     --write-partitions-to=<file> NNN.u.smt2
```

Each partition, and the cover check: `cvc5 --arrays-exp <file>`, as P1-025 ran cvc5. No
`--tlimit`: the spike stops the process at `timeoutMs` (60,000).

From `cvc5 --help` of the installed release (1.4.1, git 2b2e844), every one marked EXPERTS only:

| option | `--help` |
|---|---|
| `--compute-partitions=N` | make n partitions. n <2 disables computing partitions entirely |
| `--partition-strategy=MODE` | choose partition strategy mode |
| `--partition-when=MODE` | choose when to partition |
| `--checks-before-partition=N` | number of standard or full effort checks until partitioning |
| `--checks-between-partitions=N` | number of checks between partitions |
| `--write-partitions-to=output` | set the output channel for writing partitions |
| `--partition-check=MODE` (not set: `standard`) | select whether partitioning happens at full or standard check |
| `--partition-conflict-size=N` (not set) | number of literals in a cube; if no partition size is set, then the partition conflict size is chosen to be log2(number of requested partitions) |
| `--partition-start-time=N` (not used) | time to start creating partitions in seconds |
| `--partition-time-interval=N` (not used) | time to wait between scatter partitions |
| `--partition-tlimit=N` (not used) | time limit for partitioning in seconds |
| `--append-learned-literals-to-cubes` (not set) | emit learned literals with the cubes |
| `--random-partitioning` (not set) | create random partitions |

`--partition-strategy=help` lists six, and the spike runs all six:

| strategy | `--partition-strategy=help` |
|---|---|
| `decision-scatter` (default) | For 4 partitions, creates partitions C1, !C1 & C2, !C1 & !C2 & C3, !C1 & !C2 & !C3, from decisions |
| `heap-scatter` | the same, from heap |
| `lemma-scatter` | the same, from lemmas |
| `decision-cube` | Creates mutually exclusive cubes from the decisions in the SAT solver. |
| `heap-cube` | Creates mutually exclusive cubes from the order heap in the SAT solver. |
| `lemma-cube` | Creates mutually exclusive cubes from the lemmas sent to the SAT solver. |

`--partition-when=help`: `tlimit` (default), "Partition when the time limit is exceeded", and
`climit`, "Partition when number of checks is exceeded". The spike uses `climit` at the first
check, the earliest there is: the time trigger does not repeat, and the paper's 30-second start
would leave a partition half of a 60-second budget.

What the help does not say, seen on these files:
- A cube strategy writes 2^floor(log2 N) cubes: 8 for 8 and 16 for 24. It writes each twice.
- cvc5 writes partitions at a theory check of its main SAT solver. A query with no theory atom, or
  one its bit-vector solver settles by itself, is answered with no partition written.

## Reproduce

Windows. cvc5 1.4.1 unpacked under `.corpus/solvers/` as P1-025 fetched it (the run file of
P1-025 names the archive and its hash), and P1-025's output folder with its `.u.smt2` files and
`results.tsv`. For `--read-back`, `equiv-corpus-run`'s fetch, prepare and restore steps for
`gitextensions-8522`, with `corpus.ps1 -Env`'s variables loaded.

```powershell
dotnet build tools/spikes/cvc5-partitioning -c Release
$spike = 'tools/spikes/cvc5-partitioning/bin/Release/net10.0/Equiv.Tests.Integration.dll'
dotnet $spike --self-test <cvc5.exe>
# criteria 1 to 3: the 34, at 8 and 24 partitions, six strategies, twice
dotnet $spike --solve <cvc5.exe> <p1025 smt dir> <p1025 smt dir>/results.tsv <outDir>
# criterion 4: the 59, at 8 partitions with one strategy
dotnet $spike --solve <cvc5.exe> <p1025 smt dir> <p1025 smt dir>/results.tsv <outDir> `
  --set answered --counts 8 --strategies <strategy> --runs 1
dotnet $spike --read-back <p1025 smt dir> <p1025 smt dir>/results.tsv <outDir> <legacySolution> <modernSolution>
dotnet $spike --report <p1025 smt dir>/results.tsv <outDir>
```

`--only 5,123` keeps the queries at those positions. `<outDir>` gets `partitioned.tsv`,
`readback.tsv` and the satisfiable answers' values, which hold names and constants from the
analysed code, so point it at `.corpus/`, never at `docs/`.
