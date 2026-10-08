# P2-101 hard-queries spike (throwaway)

Measures what the queries behind the timeout Unknowns a larger budget does not decide have in
common, and whether a solver built another way answers them. The result is in
`docs/runs/2026-10-07-hard-queries.md`.

- `Program.cs`, `--export`. Loads both solutions through the production frontend and, for each
  identity of a list, asks what the ladder asks, in the ladder's order, with the production solver
  and limits: rung 1's queries on the unrolled pair (`divergence`, `opaque`, `bound`, as
  `LoopLadder.Bounded` builds them, traces compared by position), then, for a looping pair rung 1
  did not decide, rung 2's base and step obligations (as `LockstepInduction.Prove` builds them,
  traces compared as sequences). The first query of each rung that Z3 gives up on is written to
  `NNN.r1.smt2` or `NNN.r2.smt2`: the encoding's assertions and then the query's terms, as a plain
  solver prints them, with the two counts in a first comment line. Rung 1's later queries are
  written beside it (`NNN.r1-opaque.smt2`, `NNN.r1-bound.smt2`). It then runs
  `Z3Backend.Verify` on the pair, without the failure refinement, for the verdict the pair has at
  this commit. One line a pair goes to `located.tsv`.
- `Features.cs` counts an assertion set as a graph, each distinct term once: terms, sorts by kind,
  and the operators of each theory (bit-vector multiplication, division and overflow tests, array
  reads and writes, applications of the call functions and of the pure functions, datatype and
  sequence operators). It keeps numbers only.
- `Program.cs`, `--try`. Reads an exported file back in a context of its own and checks it with a
  solver made from a named list of tactics (or `default`, Z3's own solver), with the query's terms
  inlined as production inlines them (`Z3Backend.Inline`) or as written (`raw`). The check runs as a
  production check does (`SolverQuery`: a context of its own, the resource limit, the wall-clock
  backstop behind it). One line a check goes to `tried.tsv`: the answer, Z3's reason for giving up,
  the time and the resource units spent.
  A satisfiable answer counts only when every assertion, as written, is true in the model.
- `Program.cs`, `--export ... --positional-rung2 true`. Rung 2's obligations again, with the traces
  compared by position as rung 1 compares them (P1-038); one line a pair goes to `located-r2p.tsv`.
- `Program.cs`, `--replay`. Rung 1 of each pair with a variant asked only after the production
  solver gave up, and a model of `divergence` replayed with `ModelDecoder.Replay`, as rung 1
  replays its own: what the variant's answers are worth. One line a pair and variant goes to
  `replayed.tsv`.
- `report.py` prints the tables the run file quotes from `located.tsv` and `tried.tsv`.

It is not in `Equiv.slnx` and no CI gate builds it. It takes the assembly name
`Equiv.Tests.Integration` to use that project's `InternalsVisibleTo` grants, so `src/` is unchanged.

## Reproduce

Windows, after `equiv-corpus-run`'s fetch, prepare and restore steps for `gitextensions-8522`, with
`corpus.ps1 -Env`'s variables loaded. `<identities.txt>` holds one procedure identity a line.

```powershell
dotnet build tools/spikes/hard-queries -c Release
$spike = 'tools/spikes/hard-queries/bin/Release/net10.0/Equiv.Tests.Integration.dll'
dotnet $spike --export <identities.txt> <legacySolution> <modernSolution> .corpus/p2-101 --threads 4
dotnet $spike --try .corpus/p2-101 --only 3,17,40 --threads 2 `
  --variants 'pipeline=solve-eqs,simplify,propagate-values,solve-eqs,smt/inline;default=default/inline;no-inline=solve-eqs,simplify,propagate-values,solve-eqs,smt/raw;qfaufbv=qfaufbv/raw'
dotnet $spike --export <identities.txt> <legacySolution> <modernSolution> .corpus/p2-101 --positional-rung2 true --only 2,8,33
dotnet $spike --replay <identities.txt> <legacySolution> <modernSolution> .corpus/p2-101 --only 3,17,40 --variants 'smt-only=smt/raw'
python tools/spikes/hard-queries/report.py   # reads .corpus/p2-101
```

A variant is `name=tactic,tactic,.../inline` or `/raw`; a tactic may carry parameters,
`smt[relevancy=0]`. `--file .r2` tries the rung 2 files, `--rlimit` and `--timeout` set another
budget, and `--tactics` lists the tactics this Z3 has. Both modes append, and skip what their file
already holds.

The output folder holds names and constants from the analysed code, so point it at `.corpus/`,
never at `docs/`.
