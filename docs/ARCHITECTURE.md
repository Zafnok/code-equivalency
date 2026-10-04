# Architecture

## One sentence

A headless CLI takes two solution paths (or two git refs), routes them to a language
frontend that lowers each to a shared IR, matches procedures across the two, sends each
pair to a verification backend, and emits SARIF plus an exit code.

## Components and the only allowed dependency edges

```
Equiv.Cli --> Equiv.Frontend.CSharp --> Equiv.Core <-- Equiv.Verify.Z3 <-- Equiv.Cli
                (Roslyn and ICSharp-    (no Roslyn,      (Z3 lives here)
                 Code.Decompiler live    no Z3)
                 here; ADR 0039)            ^
                                            |
        Equiv.Cli ------------------> Equiv.Execute
                                  (driver processes live here;
                                   no Roslyn, no Z3; ADR 0035)

        Equiv.Cli --> Equiv.Verify.Cvc5 --> Equiv.Core
                      (the cvc5 process lives here; no Z3; ADR 0050)
```

`Equiv.Verify.Z3` does not reference `Equiv.Verify.Cvc5`: it reaches a second solver only through
`Equiv.Core`'s `ISmtSolver`, which the CLI hands it in `VerificationOptions`.

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
   .NET Framework solution. Fail loudly on any workspace diagnostic; a silent partial load is a bug.
2. Symbol enumeration -> `ProcedureIdentity` per method, constructor, property accessor.
3. Endpoint discovery -> maps ASP.NET Web API 2 / MVC 5 attribute routes and ASP.NET Core
   attribute routes to a common `HTTP VERB /template` identity.
4. Lowering: `ControlFlowGraph.Create(IOperation)` -> IR. Unsupported operations produce
   `IrOpaque` nodes, never exceptions. Coverage of the IOperation surface is tracked in
   `docs/tickets/IOPERATION-COVERAGE.md` and grows ticket by ticket.
5. IL fallback (ADR 0039; tickets P1-014 to P1-018): with `--il-fallback`, a matched pair that is
   not congruent and holds an opaque the other side does not share is lowered again, on both sides,
   from ILSpy's ILAst of the side's compilation emitted in memory (`Lowering/Il/`; no other project
   references `ICSharpCode.Decompiler`). Every type and member the ILAst names is resolved to the
   loaded compilation's symbol and goes through step 4's own `TypeMapper` and
   `CallIdentityFactory`, so identities and sorts come from the same code. The IL bodies replace the IOperation ones only when they hold fewer unshared opaques.
   Coverage is tracked in `docs/tickets/IL-COVERAGE.md`.

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
  that relies on a matched callee pair names it in the SARIF (ADR 0019). When that pair is not
  Equivalent, `IVerificationBackend.VerifyUnderContracts` proves the caller again with a
  caller-sufficient contract in place of the shared function (ADR 0036 decision 2;
  VERIFICATION-MODEL.md section 5.2).
- A second solver (ADR 0050; ticket P1-033). Z3 is the one encoder and the first solver of every query.
  When `VerificationOptions.Solver` is given, a rung 1 query Z3 gives up on (`divergence`, `opaque`,
  `bound`) is printed as SMT-LIB 2 text and asked of that `ISmtSolver`. An `unsat` is the query's
  answer. A `sat` is never a verdict: Z3 completes a model from the values it gives and the model is
  replayed as Z3's own is. Induction obligations, Horn clauses and contract queries stay with Z3.

`Equiv.Verify.Cvc5` implements `ISmtSolver` by running the `cvc5` executable as a process, once per
script. cvc5's release binary links LGPL libraries, so it is never linked, committed or shipped (ADR
0017): `equiv` runs the executable `equiv.config.json` names in `solvers.cvc5.path`, and with none
configured it asks no second solver and behaves as it did before. `tools/cvc5/fetch.ps1` fetches the
hash-pinned release for development and for CI's Windows leg.

`Equiv.Execute` runs code on the two real runtimes, the second oracle of ADR 0035: each side on its
detected runtime (ADR 0040 decision 3; P2-056). It references
`Equiv.Core` only (an architecture test enforces it):

- `Equiv.Core.Execution` holds the contract: `ExecutionRequest`, `ExecutionOutcome` and
  `IExecutionDriverFactory`. They are records and one interface, with no process code.
- `Equiv.Frontend.CSharp` implements `IExecutionDriverFactory` (`DriverFactory`) for a pair of
  runtimes, .NET Framework 4.8 and .NET 10 by default. `DriverReferences` finds any runtime's
  installed pieces: the .NET Framework targeting pack `v<version>`, or .NET's shared framework and
  `packs/Microsoft.NETCore.App.Ref/<major.minor>.*`. `DriverFactory` resolves a member against each
  side's reference assemblies and emits one driver program per side with Roslyn (`DriverRuntime`):
  an `.exe` with an `app.config` for .NET Framework, a `.dll` with a `runtimeconfig.json` and
  `rollForward: Disable` for .NET. A runtime that is not installed makes its side not constructible;
  another is never used in its place.
- `Equiv.Execute` generates inputs and runs each driver as a child process, twice per side. It
  compares the canonical outcomes. `tools/runtime-diff` (M3-032) is a thin console over it and
  `DriverFactory`, with `--from` and `--to` naming the runtimes. `WindowsRequirement` is the one
  check every execution surface asks: Windows is needed only when a side runs on .NET Framework.

`Equiv.Cli`:

- `equiv compare --legacy <path.sln> --modern <path.sln> [--baseline prev.sarif]
  [--out result.sarif] [--bound 3] [--timeout-ms 5000] [--fail-on divergent|unknown]
  [--dry-run] [--lower-only] [--execute] [--test-target 0.001] [--test-budget 10000[,60]]
  [--chc-int-mode true|false] [--invariant-model <id>] [--il-fallback] [--resource-limit <n>]`.
- `--legacy` and `--modern` mean before and after the change, on any runtime pair; `--before` and
  `--after` are aliases, and both spellings of one option are a usage error (ADR 0040 decision 4).
- `--chc-int-mode` (default true) lets loop-ladder rung 4 ask Z3 Spacer over the integers first
  (VERIFICATION-MODEL.md section 5.1); `false` keeps it to the bitvectors.
- `--invariant-model <id>` (off by default) turns on rung 5: when rung 4 times out, the Claude model `<id>` is asked
  for a coupling invariant over the Messages API (key in `ANTHROPIC_API_KEY`), which Z3 must admit
  (VERIFICATION-MODEL.md section 5.1; ADR 0036).
- `--resource-limit` overrides the config's `resourceLimit`, Z3's deterministic `rlimit` for each
  query; the config's `timeoutMs` is the wall-clock backstop (VERIFICATION-MODEL.md section 6;
  ticket P2-050). A value that is not positive is exit 3.
- `--il-fallback` (off by default until P1-018's corpus run decides otherwise; ADR 0039) turns on
  the frontend's IL fallback (step 5 above).
- Every run prints the analysed line count of each codebase and writes both to
  `run.properties.analysedLinesOfCode`: two numbers, never a total (README "Licence"). The frontend
  counts them from the files it loaded. `--dry-run` loads both sides, prints the route and the
  counts, and stops before verifying or writing SARIF.
- `--lower-only` loads, matches and lowers, writes the lowering census and the Added and
  Removed results, never calls the backend, and exits 0 unless a project was skipped (exit 4)
  (ADR 0027). It cannot be combined with `--baseline` or `--fail-on` (exit 3).
- `--execute` replays every Divergent's model on both real runtimes (ADR 0035 decision 2; ticket
  M4-009). It prints a note on stderr that code from both solutions runs on this machine, in a
  temporary working directory, and is not sandboxed. Every driver process starts in a fresh
  `cwd-*` folder under the run's `equiv-execute-*` temporary folder, so a relative write is
  deleted with it (P2-040); absolute paths, the registry and the network stay reachable. Once both
  solutions are loaded, a project on .NET Framework off Windows stops the run with exit 3, naming the
  project and its runtime (ADR 0040 decision 3; P2-056); a pair whose sides are all .NET runs on any OS.
  The frontend's analysis carries an `IReplayDriverFactory` (`Equiv.Core.Execution`) over the
  projects it loaded; the C# one emits them and compiles a driver per side for that side's project's
  detected runtime, so a same-runtime pair runs both sides on one runtime, and `Equiv.Execute`'s
  `Replayer` runs them. The result gains `properties.replay` (VERIFICATION-MODEL.md section 6);
  the verdict, rule id, fingerprint and exit code never change. It also tests every Unknown pair on
  generated inputs (decision 3; ticket P1-008): the factory's `Plan` builds the same two drivers,
  and `Equiv.Execute`'s `DifferentialTester` streams inputs through them until the Good-Turing
  discovery probability falls below `--test-target` or `--test-budget` (inputs, and optionally
  seconds, per pair) runs out. Both options are validated (exit 3) and do nothing without
  `--execute`. Without `--execute`, no user code runs.
- `equiv mcp` runs an MCP server over stdio in the same binary and container (ADR 0033; ticket M5-001),
  through the `ModelContextProtocol` SDK with its tools registered explicitly (no assembly scanning). It
  has two read-only tools that call `CompareCommand.Run`, the pipeline `equiv compare` runs, with an
  in-memory sink, so nothing is written to disk. `compare` takes `legacy`, `modern`, and optionally
  `config`, `baseline`, `bound`, `timeoutMs` (these two override the config's values and must be
  positive) and `ilFallback` (`--il-fallback`); it returns a short summary (`Equivalent n, Divergent n, Unknown n, skipped projects n,
  exit code k`, then the review list's lines; ticket P2-064), then the SARIF log as JSON text. `lower_only` takes `legacy`, `modern`, `config` and `ilFallback` and
  is `compare --lower-only`. An input error that `compare` maps to exit 3, or to exit 4 with no SARIF log,
  is a tool error (`isError: true`) with the message `equiv compare` prints on stderr. stdout carries
  protocol messages only: `CompareCommand.Run` writes its own lines through the `Streams` on
  `CompareOptions` (the console's for `compare`, stderr for `mcp`). `equiv mcp --execute` prints the same
  stderr note as `compare --execute` and registers a
  third tool, `probe` (ADR 0035, ADR 0036; ticket M5-002): an agent names a matched pair by its normalised
  identity (`{ legacy, modern, identity, arguments, culture? }`) and its own JSON arguments in parameter
  order (receiver excluded), and gets back `{ legacy: {kind, canonical}, modern: {kind, canonical}, equal
  }` from the same `IReplayDriverFactory`/`Replayer` path `--execute`'s replay uses, built from the pair's
  Roslyn method symbols rather than a solver model. `probe` never writes SARIF, never changes a `compare`
  result, and is not registered at all without `--execute`, so an agent cannot turn execution on by
  itself. Off Windows it refuses a pair with a side on .NET Framework with `compare --execute`'s message
  (P2-056).
- Router: inspects inputs, rejects mismatched or unsupported languages (exit 3), else
  selects the frontend. One frontend in the MVP; the router exists from day one so that
  Java is a new project, not a refactor.
- Exit codes: 0 all equivalent (or all results match baseline), 1 divergence, 2 unknown
  present and `--fail-on unknown`, 3 usage or unsupported input, 4 load failure, 5 internal
  error: at least one pair could not be verified, or any other unhandled exception.
- Load failure is contained to the project (ADR 0029). A C# project that fails to load, or has
  unresolved references, and any project that is not C#, is skipped. It is reported as a
  tool-execution notification, and its procedures are listed in `run.properties.unverified`. The run
  still writes every other result and then exits 4. A skipped non-C# project alone does not
  change the exit code. Exit 4 without a SARIF log means no C# project loaded on some side.
- A modern project that does not compile is not a load failure when MSBuild opens it and its
  references resolve (ADR 0029 as clarified by ticket P2-085): the loader, told which side it loads,
  keeps it. Each of its methods that does not bind is an EQ003 with reason `unbound`, every other
  method is compared, and the exit code follows the verdicts: 0, or 2 under `--fail-on unknown`,
  never 4 for that project. The same errors on the legacy side still skip its project (exit 4).
- Precedence: 5 outranks 4, and both outrank 1 and 2, because a tool fault makes the result set
  incomplete (ADRs 0023 and 0029).

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
| `ISolutionLoader` | MSBuildWorkspace | "bare" loader (parse csproj XML + reference-assembly NuGet) for Linux; a release requirement since ADR 0031 (mechanism chosen by M3-028) |
| `ILanguageFrontend` | C# | Java (Eclipse JDT sidecar) reusing everything else |
| `IVerificationBackend` | Z3 direct encoding | Boogie IVL (SymDiff-style) when loop invariants are needed |
| `IReportSink` | SARIF file | SARIF upload to GitHub Code Scanning / SonarQube |
| `IRunLog` | CLI channel writer: stderr and `--log`, heartbeat, ETA (ADR 0038, M4-012) | MCP progress notifications for `equiv mcp` (M5) |
| `IExecutionDriverFactory` | C# drivers for each side's detected runtime (ADR 0035, ADR 0040) | drivers for user assemblies (M4-009) |
