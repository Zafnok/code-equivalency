# Roadmap

Goal: a working `equiv compare` on real 4.8 → 10 solutions in one week, with the
quality gates on from the first commit. Milestones are strictly ordered; tickets inside
a milestone list their own dependencies. Ticket files: `docs/tickets/M<n>-<nnn>-<slug>.md`.

Effort labels are for a Sonnet/Opus-class agent driving, with a human reviewing PRs:
S ≤ 2h, M ≤ half day, L ≤ 1 day. Nothing is larger than L; split it if it is.

## Status (2026-09-18)

| Milestone | Planned | Actual | State |
|---|---|---|---|
| M0 Skeleton and gates | day 1 | 2026-09-18, PRs 1 to 4, 12, 17, 18 | done |
| M1 Core IR, samples, SARIF | days 2–3 | 2026-09-18, PRs 13, 15, 19, 20, 22 | done |
| M2 C# frontend | days 3–5 | | next: M2-001 |
| M3 Z3 backend and shipping | days 5–7 | | |

M0 and M1 landed in one calendar day, ahead of the three days planned. `./build.ps1` is
green on `main`: about 400 tests, 100% line and branch coverage on `Equiv.Core` and
`Equiv.Cli` (the other two `src/` projects are still empty shells). Three tickets were
added during M0 that were not in the original plan (M0-005, M0-006, M0-007), and two
review-driven docs PRs (#10, #21) rewrote every open ticket into acceptance-criteria
form after Sonnet over-scoped M1-005 from its one-paragraph goal.

### Carried forward from M0/M1

Owned by a later ticket (already written into that ticket's text):

- `MatchResult.Ambiguous` → `Unknown(UnmatchedOverload)` wiring: M3-003 (deferred by
  M1-003, M1-004 and M1-005 in turn).
- `IVerificationBackend.Verify` taking two `IrProcedure`s instead of a `ProcedurePair`
  of identities: M3-001. `ProcedurePair` gaining `OldBody`/`NewBody`: M2-003.
- Null backend in `Equiv.Cli` (matched pairs skipped, ADR 0012) replaced by `Z3Backend`
  and made non-nullable: M3-001.
- Stryker becomes a required check: M2 (see QUALITY-GATES.md, Mutation row).
- ADR 0011 (EQ003 to EQ005 emitted with `level: none`): reopen at M3-003 if real Code
  Scanning or SonarQube output shows `Unknown` results are invisible to users.
- `expected.sarif.json` per sample and the integration-test snapshot gate: M3-003.

Not yet owned by any ticket (schedule when a milestone touches the area):

- SonarQube Cloud promotion from `continue-on-error` to a required check (ADR 0009 says
  "once calibrated against a few real PRs"; six PRs have now run through it green). Blocked
  on draining the overall backlog first, which M0-008 files as GitHub issues (ADR 0016).
- SARIF driver `version`/`informationUri` and a guard against duplicate identities in one
  result set (M1-004 notes).
- A real SARIF schema validator (`Sarif.Multitool`) instead of the SDK round-trip test;
  needs an ADR 0002 row.
- Verify's `SmallRevenue` sponsorship exemption in `Directory.Build.props` expires
  2027-09; re-evaluate on monetisation.

## M0 — Skeleton and gates (day 1) — done

Everything after this milestone runs under 100% coverage and full CI.

- M0-001 (S) done, PR #1. Toolchain install + repo hygiene: .NET 10 SDK, VS 2026 Build
  Tools with 4.8 targeting pack, Docker, gh; branch renamed to `main`; `.gitignore`,
  `.gitattributes`, `global.json`, `dotnet.config`, git identity.
- M0-002 (M) done, PR #2. Solution skeleton: `Equiv.slnx`, `Directory.Build.props`,
  `Directory.Packages.props`, `.editorconfig`, the four `src/` projects and six `tests/`
  projects as empty shells with one passing test each, `build.ps1`.
- M0-003 (M) done, PR #3. Coverage + architecture gates: coverlet.MTP wiring,
  `tools/check-coverage` with 100% threshold, ArchUnitNET rules from ARCHITECTURE.md.
- M0-004 (M) done, PR #4. GitHub Actions: `ci.yml` (windows+ubuntu matrix, all blocking
  gates), `codeql.yml`, Dependabot, gitleaks, Stryker job (`continue-on-error`).
- M0-005 (S) done, PR #12. Gate hardening from the M0 review: stale-report cleanup, fail
  on missing src assembly, visible line counts, explicit `Main`, final newlines.
- M0-006 (M) done, PR #17. SonarQube Cloud changegate: code smells + duplication
  reporting and Sonar Quality Gate on PRs, non-blocking until calibrated (ADR 0009).
- M0-008 (L) in-progress. SonarQube debt triage: `tools/sonar-triage` batches Sonar's
  overall backlog into GitHub issues labelled `sonar`, filtered by a checked-in policy
  register; weekly `sonar-triage.yml`; skills `equiv-sonar-triage` and `equiv-sonar-fix`
  (ADR 0016).
- M0-007 (S) done, PR #18. Faster mutation job: PRs run Stryker incrementally (`--since`
  the base branch) with full runner concurrency; the nightly schedule stays a full sweep.

## M1 — Core IR, samples, SARIF (days 2–3) — done

- M1-001 (M) done, PR #13. Samples v1: five paired solutions — `identical`,
  `renamed-locals`, `added-branch`, `removed-null-check`, `loop-bound-change`. Each with
  a README stating expected verdicts. `samples/Directory.Build.props` isolates them from
  the root build settings.
- M1-002 (L) done, PR #15. IR types + validator + IR text format (round-trips) + IR
  interpreter (production code in Core, used for counterexample replay) + CsCheck
  generators in `tests/Equiv.TestSupport`. Also fixed `check-coverage` double-counting
  lines across test projects.
- M1-003 (M) done, PR #19. Verdict model, `IProcedureMatcher` + `StableIdentityMatcher`,
  `ProcedureIdentityNormalizer` with rename maps, `equiv.config.json` loader,
  `IVerificationBackend` stub.
- M1-004 (M) done, PR #20. SARIF writer (Sarif.Sdk) + `partialFingerprints` +
  `BaselineComputer` for all four `baselineState` values (`new` is decided by identity
  and rule id, so a verdict change is never hidden as `updated`). ADRs 0010 and 0011.
- M1-005 (S) done, PR #22. CLI shell: System.CommandLine parsing, `FrontendRouter` with
  language detection and rejection, exit codes 0 to 4, `--dry-run`, `--baseline`,
  `--fail-on`. No frontend yet.

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
- M3-003 (M) End to end on all samples; snapshots checked in; exit codes verified;
  `Ambiguous` → `Unknown(UnmatchedOverload)` wiring.
- M3-004 (M) Packaging: single-file publish, Dockerfile, `action.yml`, README usage.
  Stryker becomes blocking.
- M3-005 (S) Run against one real-world 4.8/10 pair (user-supplied); record findings
  as new tickets, not fixes.

## P1 — Loop ladder rungs 4 and 5 (first post-MVP milestone, tickets written)

- P1-001 (L) Constrained Horn clause encoding solved by Z3 Spacer for non-aligned loops.
- P1-002 (M) LLM-proposed coupling invariants, Z3-checked; pluggable model, off by default.
- P1-003 (M) Split `IrLowerer` into heap and exception collaborators, with the swapped block-map
  state as an explicit `LoweringContext` parameter (PR #30 review; behaviour-preserving).
- P1-004 (M) `foreach` over an array as an index loop (M2-004 size guard; Roslyn's CFG desugars every `foreach` into the enumerator pattern). Do P1-003 first: this ticket adds to both paths it extracts.

## Post-MVP (unordered backlog, separate tickets when scheduled)

- Bare (MSBuild-free) loader for Linux/containers.
- Floating point as IEEE sorts; `decimal`; string theory for common `string` ops.
- Boogie backend behind `IVerificationBackend` for invariant-driven unbounded loops.
- Java frontend (Eclipse JDT sidecar) reusing Core, Verify, Cli unchanged.
- Web UI: SARIF viewer + CFG split pane (React Flow). Only after users ask.
- Hosted tier: container behind an API, AKS, per-key quotas.
- SonarQube: confirm `sonar.sarifReportPaths` ingestion of EQ* rules; GitHub Code Scanning upload step in `action.yml`.
