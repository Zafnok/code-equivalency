# P1-023 loop-alignment spike (throwaway)

Measures how many of a full run's `unaligned-loop` Unknowns have a pairing of loop iterations that
runs of the two sides show, which is what P1-024 would propose and then prove. The result is in
`docs/runs/2026-10-04-loop-alignment-spike.md`. This directory is deleted if nothing builds on it.

- `Program.cs` reads the SARIF, loads both solutions through the production frontend and, for each
  `unaligned-loop` result, shares the pair's fragments as the ladder does and records: the lockstep
  rung's detail in the run, `LockstepInduction.Misalignment` at this commit, both loop forests, and
  whether a loop body holds a call, an `IrPure` or an opaque. A pair whose forests differ gets its
  cause from the structure alone. `--report` prints the tables the report quotes.
- `Tracer.cs` puts a marker call at the top of every loop header and runs the side in
  `IrInterpreter`, so the interpreter's own call trace says where each header visit falls among the
  call events. One oracle answers both sides: a call by its callee and the number of real calls
  before it, a pure function by its function and arguments.
- `Schedules.cs` runs both sides on 200 inputs (the proposer's value ranges) and searches the
  schedules "after `a` legacy and `b` modern iterations of one loop, every `m` legacy iterations
  pair with `n` modern ones", `a`, `b` in 0..2 and `m`, `n` in 1..4, simplest first. A schedule fits
  when on every usable run both sides reach the same paired points with equal call events on every
  stretch, and some run exercises it (one whole paired stretch on both sides). For a pair with no
  schedule it also says what first differs in lockstep and whether a schedule fits by callee alone.
- `SelfTest.cs` holds the ticket's two hand-written pairs: a loop against its 2:1 unrolling and
  against one peeled iteration.

It is not in `Equiv.slnx` and no CI gate builds it. It takes the assembly name
`Equiv.Tests.Integration` to use that project's `InternalsVisibleTo` grants, so `src/` is unchanged.

## Reproduce

Windows, after `equiv-corpus-run`'s fetch, prepare and restore steps for each pair, with
`corpus.ps1 -Env`'s variables loaded:

```powershell
dotnet build tools/spikes/loop-alignment -c Release
$spike = 'tools/spikes/loop-alignment/bin/Release/net10.0/Equiv.Tests.Integration.dll'
dotnet $spike --self-test
# one invocation per run; <rows>.tsv is named after the pair's slug and stays outside git:
dotnet $spike <run>/equiv.sarif <legacySolution> <modernSolution> .corpus/<slug>.tsv
# the tables, with the changed pairs of the runs together:
dotnet $spike --report 2246 .corpus/gitextensions-8522.tsv .corpus/gitextensions-9860.tsv .corpus/jellyfin-13023.tsv
```

`<run>/equiv.sarif` is a full `equiv compare` run, and the two solutions come from
`.corpus/pairs/<slug>/pair.json`. The rows hold rung details with variable names, so they stay
under `.corpus/` or a scratch folder; `--report` prints none of them.
