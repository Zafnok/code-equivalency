# P1-033 cvc5 budget measurement (throwaway)

Chooses cvc5's `--rlimit` (`Cvc5Solver.DefaultResourceLimit`) by measurement, as ticket P1-033
criterion 7 asks. The result is in `docs/runs/2026-10-04-cvc5-budget.md`.

For every `timeout` Unknown of a run's SARIF it loads the pair through the production frontend and
verifies it with the production backend (`Z3Backend.Verify`, default bound, `resourceLimit` and
`timeoutMs`): once with no second solver, and once per limit given with `Cvc5Solver` behind
`VerificationOptions.Solver`. Each script cvc5 is asked is recorded by how it ended: `sat`, `unsat`,
`unknown` (its resource limit), the wall-clock limit, or a script it could not read. It uses only the
public surface of `src/`, so what it measures is what `equiv compare` does to those pairs' rung 1
queries. It does not run the rest of `compare` (contracts, the IL fallback, execution).

It is not in `Equiv.slnx` and no CI gate builds it.

## Reproduce

Windows, after `equiv-corpus-run`'s fetch, prepare and restore steps for `gitextensions-8522`, with
`corpus.ps1 -Env`'s variables loaded, and cvc5 fetched by `tools/cvc5/fetch.ps1`.

```powershell
dotnet build tools/spikes/cvc5-budget -c Release
dotnet tools/spikes/cvc5-budget/bin/Release/net10.0/cvc5-budget.dll `
  <run>/equiv.sarif <legacySolution> <modernSolution> <outDir> `
  --cvc5 .cvc5/cvc5-Win64-x86_64-static/bin/cvc5.exe --limits 500000,2000000,8000000 --threads 6
```

`<outDir>` gets `results.tsv`, one line per pair and configuration, holding procedure identities.
Point it at `.corpus/`, never at `docs/`. `--only 3,17` keeps the results at those positions.
