# Roadmap

Goal: a working `equiv compare` on real 4.8 → 10 solutions in one week, with the
quality gates on from the first commit. Milestones are strictly ordered; tickets inside
a milestone list their own dependencies. Ticket files: `docs/tickets/M<n>-<nnn>-<slug>.md`.

Effort labels are for a Sonnet/Opus-class agent driving, with a human reviewing PRs:
S ≤ 2h, M ≤ half day, L ≤ 1 day. Nothing is larger than L; split it if it is.

## M0 — Skeleton and gates (day 1)

Everything after this milestone runs under 100% coverage and full CI.

- M0-001 (S) Toolchain install + repo hygiene: .NET 10 SDK, VS 2026 Build Tools with
  4.8 targeting pack, Docker, gh; rename branch to `main`; `.gitignore`, `.gitattributes`,
  `global.json`, `dotnet.config`, git identity.
- M0-002 (M) Solution skeleton: `Equiv.slnx`, `Directory.Build.props`,
  `Directory.Packages.props`, `.editorconfig`, the four `src/` projects and six `tests/`
  projects as empty shells with one passing test each, `build.ps1`.
- M0-003 (M) Coverage + architecture gates: coverlet.MTP wiring, `tools/check-coverage`
  script with 100% threshold, ArchUnitNET rules from ARCHITECTURE.md.
- M0-004 (M) GitHub Actions: `ci.yml` (windows+ubuntu matrix, all blocking gates),
  `codeql.yml`, Dependabot, gitleaks, Stryker job (`continue-on-error`).
- M0-005 (S) Gate hardening from the M0 review: stale-report cleanup, fail on missing
  src assembly, visible line counts, explicit `Main`, final newlines.
- M0-006 (M) SonarQube Cloud changegate: code smells + duplication reporting and Sonar
  Quality Gate on PRs, non-blocking until calibrated. Depends on ADR 0009 being accepted.
- M0-007 (S) Faster mutation job: PRs run Stryker incrementally (`--since` the base
  branch) with full runner concurrency; the nightly schedule stays a full sweep.

## M1 — Core IR, samples, SARIF (days 2–3)

- M1-001 (M) Samples v1: five paired solutions — `identical`, `renamed-locals`,
  `added-branch`, `removed-null-check`, `loop-bound-change`. Each with README stating expected verdicts.
- M1-002 (L) IR types + IR interpreter (tests only) + IR text dump format (for Verify).
- M1-003 (M) Verdict model, `IProcedureMatcher`, identity normalisation, rename map config.
- M1-004 (M) SARIF writer (Sarif.Sdk) + baseline fingerprinting + `baselineState`.
- M1-005 (S) CLI shell: argument parsing (System.CommandLine), router with
  language detection and rejection, exit codes. No frontend yet; `--dry-run` prints plan.

## M2 — C# frontend (days 3–5)

- M2-001 (L) `MSBuildWorkspace` loader with loud diagnostics; loads both sample sides
  on Windows. Integration test gate turns on.
- M2-002 (M) Symbol enumeration → `ProcedureIdentity`; Added/Removed detection end to end.
- M2-003 (L) Lowering v1: straight-line code, `if`/`else`, integer arithmetic, bool
  logic, returns, opaque calls, `IrOpaque` fallback. Lowering-oracle property test.
- M2-004 (M) Lowering v2: loops (bounded), `switch`, `throw`, null checks, fields/arrays.
- M2-005 (M) Endpoint discovery: Web API 2 / MVC 5 vs ASP.NET Core attribute routes.
- M2-006 (M) Runtime-changes table: `runtime-changes.json` of BCL members whose behaviour
  differs between .NET Framework and .NET 10 (ICU vs NLS, x87 vs SSE, hash randomisation);
  matched calls to them are EQ006, never assumed equal.

## M3 — Z3 backend and shipping (days 5–7)

- M3-001 (L) Product-program encoder + Z3 driver + counterexample decoding.
  Soundness property harness from VERIFICATION-MODEL §7.
- M3-002 (L) Loop ladder rungs 1 to 3: bounded unrolling, lockstep relational
  induction (unbounded Equivalent for aligned loops), k-induction; timeouts, `Unknown`
  reasons, `proofMethod` and `boundedBy` properties.
- M3-003 (M) End to end on all samples; snapshots checked in; exit codes verified.
- M3-004 (M) Packaging: single-file publish, Dockerfile, `action.yml`, README usage.
  Stryker becomes blocking.
- M3-005 (S) Run against one real-world 4.8/10 pair (user-supplied); record findings
  as new tickets, not fixes.

## P1 — Loop ladder rungs 4 and 5 (first post-MVP milestone, tickets written)

- P1-001 (L) Constrained Horn clause encoding solved by Z3 Spacer for non-aligned loops.
- P1-002 (M) LLM-proposed coupling invariants, Z3-checked; pluggable model, off by default.

## Post-MVP (unordered backlog, separate tickets when scheduled)

- Bare (MSBuild-free) loader for Linux/containers.
- Floating point as IEEE sorts; `decimal`; string theory for common `string` ops.
- Boogie backend behind `IVerificationBackend` for invariant-driven unbounded loops.
- Java frontend (Eclipse JDT sidecar) reusing Core, Verify, Cli unchanged.
- Web UI: SARIF viewer + CFG split pane (React Flow). Only after users ask.
- Hosted tier: container behind an API, AKS, per-key quotas.
- SonarQube: confirm `sonar.sarifReportPaths` ingestion of EQ* rules; GitHub Code Scanning upload step in `action.yml`.
