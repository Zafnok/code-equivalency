# P1-025 solver-portfolio spike (throwaway)

Measures whether a solver other than Z3 decides the queries behind a full run's `timeout` Unknowns.
The result is in `docs/runs/2026-10-04-solver-portfolio.md`.

- `Rung1.cs` builds a pair as `LoopLadder.Bounded` does (shared fragments made calls, loops unrolled
  to the bound, the product encoding) and asks rung 1's three queries, `divergence`, `opaque` and
  `bound`, with the production solver and limits. The first one Z3 gives up on is exported: what
  `Solver.ToString()` prints, with `set-logic`, `check-sat` and a `get-value` over every Bool and
  bit-vector constant around it. Nothing in the assertions is rewritten. A file holding an operator
  SMT-LIB does not define (Z3's `bvsmul_noovfl` and the like) is counted and not run.
- `ReadBack` takes a satisfiable answer's values, asserts them beside the query, lets Z3 complete
  the model, and replays it with `ModelDecoder.Replay`, the replay rung 1 runs on a model of its
  own. Only a replay that ends Divergent or Unknown(abstraction) counts.
- `Program.cs` reads the SARIF, loads both solutions through the production frontend, runs each
  solver as a process for at most `timeoutMs`, and prints the tables the report quotes. `--horn`
  counts the rung 4 Unknowns (`chc-timeout`, `chc-spurious`, `no-invariant`) of the runs given.
- `SelfTest.cs` exports one sample's query both ways it can answer and checks that Z3 answers the
  exported text as it answered the in-memory query, and that the satisfiable one's values read
  back to a Divergent.

It is not in `Equiv.slnx` and no CI gate builds it. It takes the assembly name
`Equiv.Tests.Integration` to use that project's `InternalsVisibleTo` grants, so `src/` is unchanged.

## Reproduce

Windows, after `equiv-corpus-run`'s fetch, prepare and restore steps for `gitextensions-8522`, with
`corpus.ps1 -Env`'s variables loaded. The solvers are release binaries, unpacked under `.corpus/`
and never committed; the report names each one's source and hash.

```powershell
dotnet build tools/spikes/solver-portfolio -c Release
$spike = 'tools/spikes/solver-portfolio/bin/Release/net10.0/Equiv.Tests.Integration.dll'
dotnet $spike --self-test
dotnet $spike --horn <run>/equiv.sarif <run>/equiv.sarif ...
# export only:
dotnet $spike <run>/equiv.sarif <legacySolution> <modernSolution> <outDir>
# the measurement:
dotnet $spike <run>/equiv.sarif <legacySolution> <modernSolution> <outDir> --threads 6 `
  --solver bitwuzla=<path>/bitwuzla.exe --solver cvc5=<path>/cvc5.exe
```

`<outDir>` gets one `.smt2` file per exported query and `results.tsv`. Both hold names and constants
from the analysed code, so point it at `.corpus/`, never at `docs/`.
