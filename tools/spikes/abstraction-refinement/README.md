# P1-019 abstraction-refinement spike (throwaway)

Measures how many of a full run's `abstraction` Unknowns (ADR 0026) ARDiff-style refinement would
resolve: give the tainting abstraction its real meaning and query again. The result is in
`docs/runs/2026-09-30-abstraction-spike.md`. This directory is deleted if nothing builds on it.

- `Kinds.cs` classifies each entry of `properties.abstractions`: the closed-form `IrPure` kinds
  (the only ones re-queried), floating point, `decimal`, `string`, other user-defined operators,
  and `opaque:` fragments. The pure catalogue (ADR 0025) has no integer or `bool` function, since
  those lower to `IrBinary`, so the closed-form kinds are the `op:` operators whose meaning is the
  IR's own: `IntPtr` and `UIntPtr` `==` and `!=`, which are sort equality.
- `Refiner.cs` rewrites each closed-form `IrPure` into its `IrBinary` (exception flags become
  `false`) and queries the pair twice with the run's backend and default options: as lowered
  (every abstraction shared) and refined.
- `Program.cs` reads the SARIF, loads both solutions through the production frontend, names each
  `opaque:` fragment's reason from the lowered bodies (the SARIF records only the fingerprint,
  P2-062), and prints the tables the report quotes. Identities and kinds only, never a candidate
  value or source text (`docs/runs/README.md`).
- `SelfTest.cs` holds the ticket's two hand-written pairs: `a == b` against `!(a != b)` is
  Equivalent once interpreted, and `a == b` against `a != b` Divergent. Both are
  Unknown(abstraction) with the operators shared.

It is not in `Equiv.slnx` and no CI gate builds it. It takes the assembly name
`Equiv.Tests.Integration` to use that project's `InternalsVisibleTo` grants, so `src/` is unchanged.

## Reproduce

Windows, after `equiv-corpus-run`'s fetch, prepare and restore steps for `gitextensions-8522`, with
`corpus.ps1 -Env`'s variables loaded:

```powershell
dotnet build tools/spikes/abstraction-refinement -c Release
$spike = 'tools/spikes/abstraction-refinement/bin/Release/net10.0/Equiv.Tests.Integration.dll'
dotnet $spike --self-test
# grouping only, opaque fragments not named, nothing re-queried:
dotnet $spike <run>/equiv.sarif
# the measurement:
dotnet $spike <run>/equiv.sarif <legacySolution> <modernSolution>
```

`<run>/equiv.sarif` is a full `equiv compare` run (not `--lower-only`), and the two solutions come
from `.corpus/pairs/gitextensions-8522/pair.json`.
