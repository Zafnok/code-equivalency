# P1-012 IL-lowering spike (throwaway)

Measures how many of a corpus pair's changed pairs that are still opaque after the kept M4 tickets
would lower from ILSpy's ILAst with no unmapped instruction. It also measures how often the two
sides' ILAst differs where the C# is token-identical (compiler shape drift). The result is in
`docs/runs/2026-09-28-il-lowering-spike.md`. Delete this directory if nothing builds on it.

- `Program.cs` loads both solutions through the production frontend and keeps each body's
  lowering and ADR 0024 serialisation. It picks the changed pairs that hold an unshared opaque,
  optionally joins them to a full run's SARIF, and prints counts, instruction kinds, opaque
  reasons and procedure identities, never source text (`docs/runs/README.md`).
- `IlAstReader.cs` emits each project's `Compilation` in memory and reads one method back as
  ILAst. It runs ILSpy's transform pipeline without the transforms that rebuild C# constructs.
- `MappingTable.cs` holds the table: each mapped instruction kind and the IR construct it would
  lower to.

It is not in `Equiv.slnx`, and no CI gate builds it. It takes the assembly name
`Equiv.Tests.Integration` so it can use that project's `InternalsVisibleTo` grant on
`Equiv.Frontend.CSharp`, which keeps `src/` unchanged. `ICSharpCode.Decompiler` (MIT) is
referenced here only (ADR 0002).

## Reproduce

On Windows, run `equiv-corpus-run`'s fetch, prepare and restore steps for `gitextensions-8522`
first, then load `corpus.ps1 -Env`'s variables:

```powershell
dotnet build tools/spikes/il-lowering -c Release
$spike = 'tools/spikes/il-lowering/bin/Release/net10.0/Equiv.Tests.Integration.dll'
$p = Get-Content .corpus/pairs/gitextensions-8522/pair.json -Raw | ConvertFrom-Json
$env:IL_SPIKE_DRIFT = '1'   # also list each shape-drift pair's identity and opcode trees
dotnet $spike $p.legacySolution $p.modernSolution [<run>/equiv.sarif]
```

`<run>/equiv.sarif` is optional. It comes from a full `equiv compare` run (no `--lower-only`) of
the same pair, and adds the Unknown(opaque) subset of the population.
