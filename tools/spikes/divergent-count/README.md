# P1-027 divergent-count spike (throwaway)

Measures on what share of its parameter space a Divergent pair diverges, by approximate model counting on the
formula the verdict came from. The result is in `docs/runs/2026-10-04-divergent-count-spike.md`. This directory is
deleted if nothing builds on it.

- `Inputs.cs` classifies a pair's source parameters (the shared inputs that are not synthesised): countable when
  every one is `Bool`, a bitvector, or a `Sort` used only as the key of a read of its `null.*` map; otherwise the
  first blocker, `heap-map`, `sort-by-value` or `call-result`. It also gives the bits the count projects on.
- `Counter.cs` counts the assignments of those bits for which rung 1's divergence query has a model, by the hashing
  scheme of ApproxMC2 (Chakraborty, Meel and Vardi, IJCAI 2016) with tolerance 0.8 and confidence 0.8: 72 models per
  cell, the median of 17 iterations. Fewer than 72 assignments are enumerated, which is exact. The parity rows are
  reduced by Gaussian elimination before Z3 sees them, and the solver is Z3's incremental one. One count has the
  pair's `timeoutMs` and each check its `resourceLimit`.
- `SelfTest.cs` holds ten hand-written pairs whose share is known exactly, and one more that shows the limit: an
  equality between two parameters, which Z3 gives up on under parity rows.
- `Program.cs` reads a run's EQ002 results and, when given, the EQ002 rows of the P2-047 audit, loads both
  solutions through the production frontend, and writes one tab-separated row per result. `--report` prints the
  report's tables from those rows. Identities and counts only, never a counterexample value or source text
  (`docs/runs/README.md`).

A share is over the parameter bit space, uniformly. It is not a probability that production traffic diverges.

It is not in `Equiv.slnx` and no CI gate builds it. It takes the assembly name `Equiv.Tests.Integration` to use that
project's `InternalsVisibleTo` grants, so `src/` is unchanged.

## Reproduce

Windows, after `equiv-corpus-run`'s fetch, prepare and restore steps for each pair, with `corpus.ps1 -Env`'s
variables loaded:

```powershell
dotnet build tools/spikes/divergent-count -c Release
$spike = 'tools/spikes/divergent-count/bin/Release/net10.0/Equiv.Tests.Integration.dll'
dotnet $spike --self-test
# one run; the last argument only for gitextensions-8522:
dotnet $spike <slug> <run>/equiv.sarif <legacySolution> <modernSolution> <slug>.tsv [docs/runs/2026-09-30-divergent-audit.md]
dotnet $spike --report gitextensions-8522.tsv gitextensions-9860.tsv jellyfin-13023.tsv
```

`<run>/equiv.sarif` is a full `equiv compare` run and the two solutions come from `.corpus/pairs/<slug>/pair.json`.
Write the `.tsv` files under `.corpus/` or a scratch folder, not under `docs/`.
