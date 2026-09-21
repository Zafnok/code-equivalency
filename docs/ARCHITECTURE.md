# Architecture

## One sentence

A headless CLI takes two solution paths (or two git refs), routes them to a language
frontend that lowers each to a shared IR, matches procedures across the two, sends each
pair to a verification backend, and emits SARIF plus an exit code.

## Components and the only allowed dependency edges

```
Equiv.Cli --> Equiv.Frontend.CSharp --> Equiv.Core <-- Equiv.Verify.Z3 <-- Equiv.Cli
                (Roslyn lives here)     (no Roslyn,      (Z3 lives here)
                                         no Z3)
```

`Equiv.Core` is the contract. It owns:

- **IR** (`Ir*` records): procedures, basic blocks, SSA-style instructions, a small
  type lattice (bool, bitvector ints, uninterpreted sorts), calls as opaque operations.
  Defined precisely in [VERIFICATION-MODEL.md](VERIFICATION-MODEL.md).
- **Matching contracts**: `IProcedureMatcher` produces `ProcedurePair`s (old, new) plus
  `Added` and `Removed` lists. Matching is by stable identity (namespace-normalised
  fully-qualified signature; HTTP route for endpoints). Rename maps are a config input.
- **Verdicts**: `Equivalent`, `Divergent(counterexample)`, `Unknown(reason)`,
  `Added`, `Removed`. Nothing else.
- **SARIF emission** (Sarif.Sdk) and **baseline** handling (SARIF `baselineState`).
- Interfaces: `ILanguageFrontend`, `IVerificationBackend`, `ISolutionLoader`, `IReportSink`.

`Equiv.Frontend.CSharp` implements `ILanguageFrontend`:

1. `ISolutionLoader` -> Roslyn `MSBuildWorkspace`. Since Roslyn 4.9 the workspace runs
   MSBuild in an out-of-process build host; for legacy (non-SDK) csproj it picks the
   .NET Framework host backed by VS Build Tools' MSBuild, so a .NET 10 engine can load a
   4.8 solution. Fail loudly on any workspace diagnostic; a silent partial load is a bug.
2. Symbol enumeration -> `ProcedureIdentity` per method, constructor, property accessor.
3. Endpoint discovery -> maps ASP.NET Web API 2 / MVC 5 attribute routes and ASP.NET Core
   attribute routes to a common `HTTP VERB /template` identity.
4. Lowering: `ControlFlowGraph.Create(IOperation)` -> IR. Unsupported operations produce
   `IrOpaque` nodes, never exceptions. Coverage of the IOperation surface is tracked in
   `docs/tickets/IOPERATION-COVERAGE.md` and grows ticket by ticket.

`Equiv.Verify.Z3` implements `IVerificationBackend`:

- Product-program encoding: both procedures over the same symbolic inputs; assert
  outputs differ; `unsat` means Equivalent, `sat` means Divergent with a model,
  timeout means Unknown.
- Loops go through the ladder in VERIFICATION-MODEL.md section 5.1: bounded unrolling,
  then lockstep relational induction (unbounded), then k-induction; later CHC via Spacer
  and LLM-guessed invariants. Every result is tagged `proofMethod`, and `boundedBy: k`
  when only the bounded rung succeeded.
- Uninterpreted calls: same identity on both sides means the same uninterpreted
  function (the mutual-summary assumption from SymDiff), except members listed in the
  runtime-changes table, which are Divergent (EQ006). Mapped identities via config and the
  `api-equivalences.json` catalogue (ADR 0020). The functions also take the heap at the call
  and the call's position in the trace, because callees are stateful (ADR 0018). A verdict
  that relies on a matched callee pair names it in the SARIF (ADR 0019).

`Equiv.Cli`:

- `equiv compare --legacy <path.sln> --modern <path.sln> [--baseline prev.sarif]
  [--out result.sarif] [--bound 3] [--timeout-ms 5000] [--fail-on divergent|unknown]`.
- Router: inspects inputs, rejects mismatched or unsupported languages (exit 3), else
  selects the frontend. One frontend in the MVP; the router exists from day one so that
  Java is a new project, not a refactor.
- Exit codes: 0 all equivalent (or all results match baseline), 1 divergence, 2 unknown
  present and `--fail-on unknown`, 3 usage or unsupported input, 4 load failure, 5 internal
  error: at least one pair could not be verified, or any other unhandled exception. 5 outranks
  1 and 2, because the result set is incomplete (ADR 0023).

## What is deliberately NOT in the MVP

- Desktop or web UI. SARIF is consumed by GitHub Code Scanning, the VS Code SARIF Viewer,
  and SonarQube (`sonar.sarifReportPaths`). A CFG viewer is a post-MVP ticket.
- Hosted/paid tier, AKS, API keys. The container image is the free tier; the hosted tier
  wraps the same image later.
- Cross-language comparison. The IR supports it structurally; nothing else does yet.

## Data flow

```
paths -> router -> loader(legacy) -> symbols --+
                                               +-> matcher -> pairs -> lowering -> IR pairs
                   loader(modern) -> symbols --+                                     |
                                                                                     v
          exit code <- SARIF writer <- baseline diff <- verdicts <- Z3 backend <-----+
```

## Extension points (interfaces in Equiv.Core)

| Interface | MVP implementation | Planned |
|---|---|---|
| `ISolutionLoader` | MSBuildWorkspace | "bare" loader (parse csproj XML + reference-assembly NuGet) for Linux |
| `ILanguageFrontend` | C# | Java (Eclipse JDT sidecar) reusing everything else |
| `IVerificationBackend` | Z3 direct encoding | Boogie IVL (SymDiff-style) when loop invariants are needed |
| `IReportSink` | SARIF file | SARIF upload to GitHub Code Scanning / SonarQube |
