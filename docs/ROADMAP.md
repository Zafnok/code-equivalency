# Roadmap

Goal: a working `equiv compare` on real 4.8 → 10 solutions in one week, with the
quality gates on from the first commit. Milestones are strictly ordered; tickets inside
a milestone list their own dependencies. Ticket files: `docs/tickets/M<n>-<nnn>-<slug>.md`.

Effort labels are for a Sonnet/Opus-class agent driving, with a human reviewing PRs:
S ≤ 2h, M ≤ half day, L ≤ 1 day. Nothing is larger than L; split it if it is.

## Status (2026-09-21)

| Milestone | Planned | Actual | State |
|---|---|---|---|
| M0 Skeleton and gates | day 1 | 2026-09-18 to 2026-09-21, PRs 1 to 4, 12, 17, 18, 32, 68, 73, 76 | done |
| M1 Core IR, samples, SARIF | days 2–3 | 2026-09-18, PRs 13, 15, 19, 20, 22 | done |
| M2 C# frontend | days 3–5 | 2026-09-18 to 2026-09-20, PRs 24, 25, 27, 30, 59, 67 | done |
| M3 Z3 backend and shipping | days 5–7 | M3-001 PR #78 | next: M3-014 and M3-002 in parallel |

M0 and M1 landed in one calendar day and M2 in three, still ahead of the five days planned.
`main` is green in CI on Windows and Ubuntu with 100% line and branch coverage on every `src/`
project (`Equiv.Verify.Z3` is still an empty shell until M3-001). Three gate tickets were added after M2's plan
(M0-009 licensing, M0-010 licence gate, M0-011 blocking mutation gate). Mutation scores on
the 2026-09-20 nightly were Core 96.5%, Frontend.CSharp 96.4%, Cli 69.3% (97.7% after M0-011).
Reviews of M2-003 and M2-004 produced ADRs 0014 and 0015 and the P1-003 to P1-006 tickets.

### Carried forward from M0 to M2

Owned by a later ticket (already written into that ticket's text):

- `MatchResult.Ambiguous` → `Unknown(UnmatchedOverload)` wiring: M3-003 (deferred by
  M1-003, M1-004 and M1-005 in turn).
- `IVerificationBackend.Verify` taking two `IrProcedure`s instead of a `ProcedurePair`
  of identities: M3-001. `ProcedurePair` gaining `OldBody`/`NewBody`: M2-003.
- Null backend in `Equiv.Cli` (matched pairs skipped, ADR 0012) replaced by `Z3Backend`
  and made non-nullable: M3-001.
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
- M3-004 carries the rest of the licensing work as its criteria 7 to 9: notices in the artifacts,
  a per-release Change Date, and the MSBuild redistribution question (VS Build Tools is not
  freely redistributable in a container image).

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
- M0-008 (L) done, PR #32. SonarQube debt triage: `tools/sonar-triage` batches Sonar's
  overall backlog into GitHub issues labelled `sonar`, filtered by a checked-in policy
  register; weekly `sonar-triage.yml`; skills `equiv-sonar-triage` and `equiv-sonar-fix`
  (ADR 0016).
- M0-007 (S) done, PR #18. Faster mutation job: PRs run Stryker incrementally (`--since`
  the base branch) with full runner concurrency; the nightly schedule stays a full sweep.
- M0-009 (M) done, PR #68. Licensing: `LICENSE` (BUSL-1.1, three-seat and
  50k-LOC-per-codebase free tier, converting to Apache-2.0 on 2030-09-20),
  `THIRD-PARTY-NOTICES.md`, `CONTRIBUTING.md`, package metadata, and ADR 0017 with the
  dependency licence allowlist. The repo had been public with no licence at all since 2026-09-18.
- M0-010 (M) done, PR #73. Dependency licence gate: `tools/licence-check` reads real licences
  from the lock files, fails the build outside the ADR 0017 allowlist, and generates
  `THIRD-PARTY-NOTICES.md`. Landed before M3-001 as required. (#73 merged before its CI
  finished and turned `main` red; fixed in #74, and the `main` ruleset now has required checks.)
- M0-011 (S) done, PR #76. Blocking mutation gate: `mutation.yml` fails below `--break-at 90` and is
  no longer `continue-on-error`; `Equiv.Cli` raised from 69% to 98%. Pulled forward from
  M3-004 criterion 5, since M3 is where the solver code lands.

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

## M2 — C# frontend (days 3–5) — done

- M2-001 (L) done, PR #24. `MSBuildWorkspace` loader with loud diagnostics; loads both
  sample sides on Windows. Integration test gate turns on.
- M2-002 (M) done, PR #25. Symbol enumeration → `ProcedureIdentity`; Added/Removed
  detection end to end.
- M2-003 (L) done, PR #27. Lowering v1: straight-line code, `if`/`else`, integer
  arithmetic, bool logic, returns, opaque calls, `IrOpaque` fallback. Lowering-oracle
  property test. Its review produced ADR 0014 (reaching `IrOpaque` makes the outcome unknown).
- M2-004 (M) done, PR #30. Lowering v2: loops (bounded), `switch`, `throw`, null checks,
  fields/arrays. Its review produced ADR 0015 (heap-model limits, P1-005/P1-006) and P1-003.
- M2-005 (M) done, PR #59. Endpoint discovery: Web API 2 / MVC 5 vs ASP.NET Core attribute
  routes.
- M2-006 (M) done, PR #67. Runtime-changes table: `runtime-changes.json` of BCL members whose behaviour
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
- M3-005 (S) Run against one real-world 4.8/10 pair (user-supplied); record findings
  as new tickets, not fixes.
- M3-006 (S) Report analysed lines of code per codebase, so the BUSL free tier's 50,000-line
  limit is observable. Reporting only, no enforcement (ADR 0017).

Added by the pre-M3 architecture review (2026-09-21; ADRs 0018 to 0020), which found three
silent false-Equivalent paths in the spec and a precision gap that would make the first real
run almost all Unknown:

- M3-007 (M) Field and array maps are `Ref` parameters, so the final heap is observable
  (ADR 0018).
- M3-008 (S) Every verdict names the matched callee pairs it assumed, and flags the unproven
  ones (ADR 0019).
- M3-009 (M) `api-equivalences.json`: cited overload-drift and Web API result equivalences,
  applied on the legacy side and listed on each result (ADR 0020).
- M3-010 (M) Property access as accessor calls; implicit upcasts and boxing as cast maps.
- M3-011 (L) `foreach`, `using` and constructors lowered through the CFG instead of whole-body
  opaque.
- P1-005 and P1-006 are promoted into M3 (ADR 0018): a call reads and writes the heap,
  and arrays are keyed by value. P1-003 comes with them as their shared prerequisite.

Added by the M3-001 review (2026-09-21; ADR 0021):

- M3-012 (S) The synthesised-input naming rule ADR 0021 relies on is enforced over `samples/`,
  and a C# parameter named `@this` no longer collides with the receiver input.

Added by ADR 0023 (2026-09-21):

- M3-013 (S) A pair whose verification throws is reported as a SARIF tool-execution
  notification and skipped; the run exits 5, and its baseline result is carried as `unchanged`.

Added by the Unknown-rate review (2026-09-21; ADRs 0024 to 0027). The M3 plan would
ship a sound tool whose Unknown rate grows with codebase size, and it measures that rate last.
These tickets measure it first, make unchanged code free, and keep EQ002 exact while the engine
abstracts more:

- M3-014 (M) Lowering census in every SARIF run, `--lower-only`, and a `business-layer` sample
  that every precision ticket must move towards its target verdicts (ADR 0027).
- M3-022 (S) Census of the user's real 4.8/10 pair; its histogram reorders the precision list
  (ADR 0027). No engine changes.
- M3-015 (M) Bound fingerprints: equal, non-runtime-sensitive bodies are Equivalent by
  `congruence` without the solver (ADR 0024).
- M3-016 (M) Replay taint: a divergence that depends on an abstraction is Unknown(Abstraction)
  with a candidate counterexample, never EQ002 (ADR 0026).
- M3-023 (S) Unknown results point at the opaque or abstract lines, not the method (ADR 0027).
- Precision (default order; M3-022 reorders it by pairs unlocked per effort point):
  M3-010, M3-011, M3-018 (L, `IrPure` for float, decimal and operators, ADR 0025),
  M3-019 (M, `ref`/`out` call arguments and `lock`), M3-017 (L, fragments on both sides are
  shared calls, ADR 0024), M3-020 (M, type tests and downcasts), M3-021 (M, `await`).

Order:
1. **Measure first, in parallel with M3-002:** M3-014 → M3-022. This needs no Z3 and answers
   "is the Unknown rate survivable" before any more engine work.
2. **Soundness (unchanged):** M3-001 → M3-002; M3-007 → P1-003 → P1-006 → P1-005 (P1-005 also
   needs M3-001); M3-009; M3-012 and M3-013 once M3-001 is merged.
3. **Blast radius, before any snapshot is taken:** M3-015 (needs M3-014, M3-001, M3-009);
   M3-016 (needs M3-001) → M3-023.
4. M3-003 (needs M3-002, M3-007, M3-009, M3-012, M3-013, M3-015, M3-016, M3-023, P1-005, P1-006)
   → M3-008 → M3-004.
5. **Precision, in M3-022's order:** M3-010 → M3-011 and M3-020 (after M3-010) and M3-021
   (after M3-011); M3-018 (after M3-016); M3-019 (after P1-005); M3-017 (after M3-015, M3-016,
   P1-005). Each updates the `business-layer` snapshot.
6. M3-005 (needs M3-004, M3-008 and the precision tickets M3-022 kept) → M3-006.

## P1 — Loop ladder rungs 4 and 5 (first post-MVP milestone, tickets written)

- P1-001 (L) Constrained Horn clause encoding solved by Z3 Spacer for non-aligned loops.
- P1-002 (M) LLM-proposed coupling invariants, Z3-checked; pluggable model, off by default.
- P1-003 (M) (promoted into M3) Split `IrLowerer` into heap and exception collaborators, with the swapped block-map
  state as an explicit `LoweringContext` parameter (PR #30 review; behaviour-preserving).
- P1-004 (M) `foreach` over an array as an index loop (M2-004 size guard; Roslyn's CFG desugars every `foreach` into the enumerator pattern). Do P1-003 first: this ticket adds to both paths it extracts.
- P1-005 (L) (promoted into M3) An `IrCall` reads and writes the heap: its effects and
  results are functions of the callee, its arguments, the heap at the call and its position,
  shared by both sides (ADRs 0015, 0018).
- P1-006 (L) (promoted into M3) Array element and length maps keyed by the array value instead of the array
  variable, so two variables holding one array are one slice (ADR 0015).

P1-005 and P1-006 are the two soundness limits ADR 0015 names. ADR 0018 moves both ahead
of M3-003, so no build that reports sample verdicts carries them. Until they land, an
Equivalent verdict on a procedure that writes a field around a call, or that takes two
array parameters, rests on an assumption no gate can see; M3-001's soundness harness runs
over IR and does not cover them.

## Post-MVP (unordered backlog, separate tickets when scheduled)

- Bare (MSBuild-free) loader for Linux/containers.
- Floating point as IEEE sorts; `decimal`; string theory for common `string` ops.
- Boogie backend behind `IVerificationBackend` for invariant-driven unbounded loops.
- Java frontend (Eclipse JDT sidecar) reusing Core, Verify, Cli unchanged.
- Web UI: SARIF viewer + CFG split pane (React Flow). Only after users ask.
- Hosted tier: container behind an API, AKS, per-key quotas.
- SonarQube: confirm `sonar.sarifReportPaths` ingestion of EQ* rules; GitHub Code Scanning upload step in `action.yml`.
