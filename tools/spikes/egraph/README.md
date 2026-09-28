# P1-011 equality-saturation spike (throwaway)

Measures how many of a corpus pair's changed pairs differ only where a fixed rule set closes the
difference, which is what "congruence modulo rewrites" would add over ADR 0024's congruence. The
result is in `docs/runs/2026-09-28-egraph-spike.md`. This directory is deleted if nothing
builds on it.

- `OpTree.cs` parses `BoundSerialiser`'s canonical serialisation (ADR 0024) back into a tree.
- `EGraph.cs` is a minimal e-graph with the ticket's rules, each guarded so that it is an IR
  identity where it fires (integral and `bool` operators, no user-defined operator; operands
  reordered only when both are pure).
- `Differ.cs` walks both trees and asks the e-graph about each smallest differing subtree.
- `Program.cs` loads both solutions through the production frontend and joins the pairs to a
  full run's SARIF. It prints counts, rule names and node kinds only (`docs/runs/README.md`).

It is not in `Equiv.slnx` and no CI gate builds it. It takes the assembly name
`Equiv.Tests.Integration` to use that project's `InternalsVisibleTo` grant on
`Equiv.Frontend.CSharp`, so `src/` is unchanged.

## Reproduce

Linux or Windows, after `equiv-corpus-run`'s fetch, prepare and restore steps for
`gitextensions-8522`, with `corpus.ps1 -Env`'s variables loaded (on Linux, also
`EnableWindowsTargeting=true`):

```sh
dotnet build tools/spikes/egraph -c Release
spike=tools/spikes/egraph/bin/Release/net10.0/Equiv.Tests.Integration.dll
dotnet $spike --self-test
# upper bound over every changed pair, no solver needed:
dotnet $spike <legacySolution> <modernSolution>
# the measured population: pairs a full run left Unknown(opaque)
dotnet $spike <legacySolution> <modernSolution> <run>/equiv.sarif
```

`<legacySolution>` and `<modernSolution>` come from `.corpus/pairs/gitextensions-8522/pair.json`.
`<run>/equiv.sarif` comes from a full `equiv compare` run (no `--lower-only`) over the same pair at the
same commit.
