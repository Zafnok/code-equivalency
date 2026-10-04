# P1-021 failure-refinement spike (throwaway)

Measures ADR 0037's two queries (`newFailures`, `removedFailures`; ticket P1-013): what they answer
on a full run's Unknowns today, and what they would answer on its `timeout` Unknowns, which the
backend does not ask. The result is in `docs/runs/2026-10-04-failure-refinement.md`. This directory
is deleted if nothing builds on it.

- `Tabulation.cs` reads each run's SARIF and counts `properties.failureRefinement.newFailures` and
  `removedFailures` by outcome and by `unknownReason`. An Unknown with no `failureRefinement` is
  `not queried`.
- `TimeoutQueries.cs` takes the `timeout` Unknowns of one run, loads both solutions through the
  production frontend with the default config (as P1-019's spike does), verifies each pair once at
  this commit (the baseline) and then asks the two queries at the default `resourceLimit`.
- `Measured.cs` runs the production `FailureRefinementQuery` on a pair, with none of it copied. The
  time of each query's solver checks, and whether one gave up, come from the `check:` lines it writes
  to the run log at debug (ADR 0038). A query whose answer is `unknown` because a check gave up is
  recorded as `timeout`.
- `SelfTest.cs` holds the ticket's two hand-written pairs: `a + 1` against `a + 2`, where both
  queries are unsatisfiable (`none-proved`), and a body that returns against one that throws when
  its argument is 0, where `newFailures` has a model (`found`).

It prints identities, reasons and counts only, never a model value or source text
(`docs/runs/README.md`). It is not in `Equiv.slnx` and no CI gate builds it. It takes the assembly
name `Equiv.Tests.Integration` to use that project's `InternalsVisibleTo` grants, so `src/` is
unchanged.

## Reproduce

Windows, after `equiv-corpus-run`'s fetch, prepare and restore steps for `gitextensions-8522`, with
`corpus.ps1 -Env`'s variables loaded:

```powershell
dotnet build tools/spikes/failure-refinement -c Release
$spike = 'tools/spikes/failure-refinement/bin/Release/net10.0/Equiv.Tests.Integration.dll'
dotnet $spike --self-test
# criterion 1, one SARIF per run:
dotnet $spike tabulate <run>/equiv.sarif <run>/equiv.sarif <run>/equiv.sarif
# criterion 2; one line per pair goes to stderr as it finishes, the tables to stdout:
dotnet $spike timeouts <run>/equiv.sarif <legacySolution> <modernSolution> [<threads, default 4>]
```

Each `<run>/equiv.sarif` is a full `equiv compare` run (not `--lower-only`), and the two solutions
come from `.corpus/pairs/gitextensions-8522/pair.json`.
