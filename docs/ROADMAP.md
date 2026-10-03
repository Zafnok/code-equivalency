# Roadmap

Goal: a working `equiv compare` on real 4.8 → 10 solutions in one week, with the
quality gates on from the first commit. Milestones are strictly ordered; tickets inside
a milestone list their own dependencies. Ticket files: `docs/tickets/M<n>-<nnn>-<slug>.md`
(open) and `docs/tickets/done/` (finished).

Effort labels are for a Sonnet/Opus-class agent driving, with a human reviewing PRs:
S ≤ 2h, M ≤ half day, L ≤ 1 day. Nothing is larger than L; split it if it is.

## Status (2026-09-23)

| Milestone | Planned | Actual | State |
|---|---|---|---|
| M0 Skeleton and gates | day 1 | 2026-09-18 to 2026-09-21, PRs 1 to 4, 12, 17, 18, 32, 68, 73, 76 | done, except M0-012 (added 2026-09-24; needed by M3-003) |
| M1 Core IR, samples, SARIF | days 2–3 | 2026-09-18, PRs 13, 15, 19, 20, 22 | done |
| M2 C# frontend | days 3–5 | 2026-09-18 to 2026-09-20, PRs 24, 25, 27, 30, 59, 67 | done, except M2-007 (added 2026-09-24; needed by M3-003) |
| M3 Z3 backend and shipping | days 5–7 | M3-001 PR #78 | next: M3-014 and M3-024, in parallel with M3-002 |
| M4 Precision and first corpus run | after M3 | | order set by M3-022's corpus census |
| M5 Agent surface (MCP) | after M3-004, parallel with M4 | | M5-001 written |
| M6 Hosted tier on Container Apps | after M3-004, parallel with M4 and M5 | M6-001 PR #248 | next tickets unwritten |

M0 and M1 landed in one calendar day and M2 in three, still ahead of the five days planned.
`main` is green in CI on Windows and Ubuntu with 100% line and branch coverage on every `src/`
project (`Equiv.Verify.Z3` is still an empty shell until M3-001). Three gate tickets were added after M2's plan
(M0-009 licensing, M0-010 licence gate, M0-011 blocking mutation gate). Mutation scores on
the 2026-09-20 nightly were Core 96.5%, Frontend.CSharp 96.4%, Cli 69.3% (97.7% after M0-011).
Reviews of M2-003 and M2-004 produced ADRs 0014 and 0015 and the P1-003 to P1-006 tickets.

The 2026-09-23 feasibility review found three things. The one-week plan is at day 7 with about 21
tickets left. The census that decides feasibility had no input, because no real pair was ever
named. And one unloadable project aborts a whole solution. ADR 0028 moves every real-code
run to a pinned public corpus (`tools/corpus/`, run only through the `equiv-corpus-run` skill) and
fixes the success thresholds before any data exists. ADR 0029 bounds every failure to a project, a
method or a line, and makes each Unknown say which (tickets M3-024, M3-025, M4-008). A preview run
that day on the corpus pair `eshop-upgrade-assistant` aborted with exit 4, as expected before
M3-024. Sonar issue batches wait until M3-022's verdict, because they polish code whose future the
census decides.

The 2026-09-24 second-oracle review followed the census verdict ("continue"). It found three
gaps that more solver work cannot close:
- About 76% of Git Extensions' changed pairs contain an opaque node.
- On the agent pairs, which are pure retargets, congruence decides nearly every verdict. It rests
  on a 14-row, hand-picked `runtime-changes.json`, so any runtime change the table misses is a
  silent false Equivalent.
- Nothing yet checks a verdict against the code as the CLR runs it. Section 7's soundness harness
  generates IR, and seeded recall waits for M4-007.

Three proposed ADRs respond:
- ADR 0035: the real runtimes are a second oracle. Execution measures the runtime table,
  confirms counterexamples and bounds Unknowns, and never proves.
- ADR 0036: a proposed invariant, contract or table row is a hypothesis until a checker admits
  it, and relational callee contracts can replace unproven assumptions.
- ADR 0037: an Unknown says whether the modern side can fail where the legacy side does not.

Tickets were added to every milestone they belong to:
- M0-012: a differential soundness gate over generated C# pairs.
- M2-007: `runtime-changes.json` made complete against Microsoft's breaking-change pages.
- M3-032 and M3-033: measure the BCL on both runtimes.
- M4-009: counterexample replay.
- M4-010: mechanical seeds on the corpus.
- M5-002: an MCP `probe` tool.
- P1-008 to P1-013: tested Unknowns, trace-mined invariants, callee contracts, two measuring
  spikes (equality saturation, IL lowering) and failure refinement.
- P2-018: a vacuous side is a load failure.

M0-012 and M2-007 are MVP-blocking: M3-003 now needs both. M3-033, M4-009 and M4-010 are needed
by M4-007.

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
- M0-012 (L) Differential soundness gate: generated C# method pairs (one of them mutated) are
  compiled and run, then verified by the real frontend and Z3. An observed divergence is never
  Equivalent, a Divergent's model replays as a divergence, and a semantics-preserving mutation is
  never Divergent. 200 pairs per PR, 5,000 nightly. Added 2026-09-24. It covers the gap between C#
  and the verdict, which section 7's IR-level harness cannot see by construction. Needs M3-002,
  M3-007, P1-006. M3-003 needs it.

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
- M2-007 (M) `runtime-changes.json` complete against Microsoft's breaking-change pages. Every
  behavioural entry for .NET Core 3.0 to .NET 10 is a row or a reasoned exclusion in
  `docs/runtime-changes-review.md`, and every row carries `source`. Added 2026-09-24: on retargets,
  congruence is only as sound as this table (ADR 0024), and it has 14 rows. M3-003 needs it.

## M3 — Z3 backend and shipping (days 5–7)

M3 ships a sound tool that measures its own Unknown rate. Making it precise on real code is M4.
M3 grew from six tickets to twenty-two through three reviews (below). On 2026-09-21 it was
consolidated. The precision tickets moved to M4 and were renumbered (M3-011, M3-018, M3-019,
M3-017, M3-020, M3-021 and M3-005 became M4-001 to M4-007). Four pairs merged: M3-006 into M3-014,
M3-008 into M3-015, M3-012 into M3-007, and M3-023 into M3-016. On 2026-09-23 ADR 0029 added
M3-024 and M3-025, and M3-014's review found the IR007 lowering bug M3-026 fixes. The dependency
and architecture audit added ADR 0030 (Z3 5.1 from the official GitHub release, M3-027) and ADR 0031
(Linux parity before release, M3-028). Sixteen tickets remain open, plus the three promoted P1
tickets.

- M3-001 (L) done, PR #78. Product-program encoder + Z3 driver + counterexample decoding.
  Soundness property harness from VERIFICATION-MODEL §7.
- M3-002 (L) done, PR #121. Loop ladder rungs 1 to 3: bounded unrolling, lockstep relational
  induction (unbounded Equivalent for aligned loops), k-induction; timeouts, `Unknown`
  reasons, `proofMethod` and `boundedBy` properties.
- M3-003 (M) End to end on all samples; snapshots checked in; exit codes verified;
  `Ambiguous` → `Unknown(UnmatchedOverload)` wiring.
- M3-004 (M) Packaging: single-file publish, Dockerfile, `action.yml`, README usage.
- M3-027 (M) Z3 4.12.2 to 5.1.0 from the official GitHub release through a hash-pinned local feed;
  drops the PyPI `libz3.so` workaround (ADR 0030).
- M3-028 (M) Spike: which loader reaches Linux parity on the samples and one corpus pair; writes
  M3-029, the loader itself, and the Windows/Ubuntu SARIF parity job (ADR 0031). Chose candidate
  2, the only one that matched Windows on all 11 solutions (ADR 0031 Clarification 2026-09-25).
- M3-029 (L) The Linux loader. Off Windows, non-SDK projects load through a bare loader and
  SDK-style projects through MSBuildWorkspace on the .NET SDK. Windows is unchanged. Also adds the
  Windows/Ubuntu SARIF parity job. The Linux image needs the SDK.
- M3-007 (L) Synthesised inputs: field and array maps are `Ref` parameters, so the final heap is
  observable (ADR 0018). The naming rule ADR 0021 relies on is enforced over `samples/`, and a C#
  parameter named `@this` no longer collides with the receiver input.
- M3-009 (M) `api-equivalences.json`: cited overload-drift and Web API result equivalences,
  applied on the legacy side and listed on each result (ADR 0020).
- M3-010 (M) Property access as accessor calls; implicit upcasts and boxing as cast maps. It stays
  in M3 because M3-009's samples need it.
- M3-013 (S) A pair whose verification throws is reported as a SARIF tool-execution
  notification and skipped; the run exits 5, and its baseline result is carried as `unchanged`
  (ADR 0023).
- M3-014 (L) Lowering census and per-codebase analysed line counts in every SARIF run,
  `--lower-only`, and a `business-layer` sample that every precision ticket must move towards
  its target verdicts (ADR 0027). The line counts make the BUSL free tier's 50,000-line limit
  observable. They are reported only, never enforced (ADR 0017).
- M3-015 (L) Bound fingerprints: equal, non-runtime-sensitive bodies are Equivalent by
  `congruence` without the solver (ADR 0024). Every verdict names the matched callee pairs it
  assumed, and flags the unproven ones (ADR 0019).
- M3-016 (L) Replay taint: a divergence that depends on an abstraction is Unknown(Abstraction)
  with a candidate counterexample, never EQ002 (ADR 0026). Unknown results point at the opaque
  or abstract lines, not the method (ADR 0027).
- M3-022 (S) Census of the public corpus (ADR 0028): the Git Extensions 4.8-to-.NET 5 pair and three
  Poly-MigrationBench repos migrated by an agent, run in the skill's `census` mode. It scores ADR
  0028's criteria (continue, re-scope or stop), and its histogram orders M4 (ADR 0027). No engine
  changes.
- M3-024 (M) A project that fails to load, or is not C#, is skipped instead of aborting the solution.
  A method whose bound body is erroneous is `Unknown(Unbound)` and never congruent (ADR 0029). Needed
  before M3-022, because the first corpus solution holds `.vcxproj` and `.wixproj` projects.
- M3-025 (M) Every Unknown carries `scope` (`line` or `method`), and a `line` one carries the residual
  claim ADR 0014's first query already proves. Whole-body opaques point at their construct, not the
  method (ADR 0029).
- M3-026 (S) An `async` method (`Task`, `Task<T>`, or `void`) lowers to one whole-body opaque instead
  of the ill-typed IR an `async Task<T>` produces today (IR007, found during M3-014). Needs M3-022,
  so the census measures real opaque reasons before this collapses them into one; must land before
  M3-013 and before any full verifying run on real code.
- M3-030 (M) The census reports changed pairs (not congruent, by a token-identical proxy until
  M3-015), exact opaque reason sets per changed pair, runtime-changes call exposure and, in
  `corpus.ps1`, NuGet package drift. The lowerable-share rules are evaluated on changed pairs
  (ADR 0034).
- M3-031 (S) Census rerun on the Git Extensions human pair, scored under ADR 0034. It is the
  feasibility test M3-022 could not complete, and it orders M4 by exact pairs unlocked. Needs
  M3-030, P2-010, P2-011, P2-012. It also carries P2-010's corpus check (its criterion 7): P2-010
  was fixed and unit-tested on Linux, where the census cannot run.
- P1-003, P1-005 and P1-006 are promoted into M3 (ADR 0018): a call reads and writes the heap,
  and arrays are keyed by value. P1-003 comes with them as their shared prerequisite.
- M3-032 (L) `Equiv.Execute` and `tools/runtime-diff` (ADR 0035 decision 1). A BCL member is
  called with generated arguments under .NET Framework 4.8 and .NET 10, in child processes built
  from generated drivers, under five cultures, with each input run twice per side. Windows only.
  Needs M2-007 and ADR 0035 accepted.
- M3-033 (M) The census lists every BCL member a lowered body calls. `runtime-diff` runs on the
  most-called ones from the corpus pairs, and the ones that differ become `measured` rows with a
  witness. Needs M3-032, M3-030. M4-007 needs it.

Where the tickets came from: the pre-M3 architecture review (ADRs 0018 to 0020) found three
silent false-Equivalent paths in the spec and a precision gap. The 2026-09-23 feasibility review
added ADRs 0028 and 0029 and tickets M3-024, M3-025 and M4-008. The M3-001 review added ADR 0021,
and ADR 0023 added M3-013. The Unknown-rate review (ADRs 0024 to 0027) found that the plan would
ship a sound tool whose Unknown rate grows with codebase size, and that it measured that rate
last. So measuring comes first, unchanged code is free, and EQ002 stays exact while the engine
abstracts more.

Order:
1. **Measure first, in parallel with M3-002:** M3-014 and M3-024 (independent), then M3-022 on the
   public corpus. This needs no Z3 and answers "is the Unknown rate survivable" before any more
   engine work. If M3-022's verdict is re-scope or stop, everything below waits for a new ADR.
   M3-026 comes right after M3-022: running the census first keeps today's real per-reason opaque
   counts (`Await`, `PropertyReference`, ...) instead of collapsing them into one `async` reason.
   M3-022 ended "incomplete: human pair pending", so ADR 0034 adds M3-030, then M3-031 (after
   P2-011, P2-010 and P2-012). M3-031's verdict takes the place M3-022's had: re-scope or stop
   there also waits for a new ADR.
2. **Soundness:** M3-002 → M3-027; M3-007 → P1-003 → P1-006 → P1-005; M3-010 → M3-009; M3-026 → M3-013 (an
   `async Task<T>` pair must stop being ill-typed IR before a pair can throw its way into M3-013's
   exit-5 path, and before any full verifying corpus run).
3. **Blast radius, before any snapshot is taken:** M3-015 (needs M3-014, M3-009, M3-024); M3-016
   (needs M3-014); M3-025 (needs M3-016, M3-024).
4. **Linux parity, in parallel with 2 and 3:** M3-028 (needs M3-024) → M3-029.
5. **Second oracle, in parallel with 2 to 4:** M0-012 and M2-007 now (both independent; M3-003
   needs both). Once ADR 0035 is accepted: M3-032 → M3-033. These don't block M3-004, but M4-007
   needs M3-033.
6. M3-003 (needs M0-012, M2-007, M3-002, M3-007, M3-009, M3-013, M3-014, M3-015, M3-016, M3-024,
   M3-025, P1-005, P1-006) → M3-004 (also needs M3-027 and M3-029). M3-004 is the gate for M4-007,
   M5 and M6.

## M4 — Precision and the first corpus run

M4 makes the first real run say something. Without these tickets it is mostly `Unknown(opaque)`.
Reordered by pairs unlocked per effort point (S=1, M=2, L=4), from the Git Extensions census
(`docs/runs/2026-09-24-census-verdict.md`, ADR 0034: pairs unlocked is exact over changed pairs,
not an upper bound over matched pairs). A ticket under ADR 0028's bar, now 5% of changed pairs
(16 of 315), moved to the Post-MVP backlog below: M4-003, M4-005, M4-006, M4-008. P2-001 cleared
the bar and is scheduled in below, keeping its ticket id. Soundness dependencies still win; none
of the tickets below depend on each other, so the order is exactly the pairs-unlocked ranking.
Each precision ticket updates the `business-layer` snapshot.

The 2026-09-23 census (`docs/runs/2026-09-23-census-verdict.md`) could not reorder this list:
Git Extensions crashed, and the three agent pairs are pure retargets with no changed `.cs` file.
M3-031 reran the census after P2-011, P2-010 and P2-012 fixed the crash and produced the first
changed-pair data, which is what reorders this list.

- M4-001 (L) `foreach`, `using` and constructors lowered through the CFG instead of whole-body
  opaque. Needs M3-007, M3-010. Unlocks 54 changed pairs (17.1%).
- P2-001 (M) `new T[n]` and array initialisers lowered (reason `ArrayCreation`). Needs P1-006.
  Unlocks 18 changed pairs (5.7%); promoted from P2 into M4 by the 2026-09-24 census.
- M4-004 (L) Fragments on both sides are shared calls (ADR 0024). Needs M3-015, M3-016, P1-005.
  Unlocks 25 changed pairs (7.9%).
- M4-002 (L) `IrPure` for float, decimal and user-defined operators (ADR 0025). Needs M3-015,
  M3-016. Unlocks 19 changed pairs (6.0%).
- M4-009 (M) `--execute`: every Divergent's model is replayed on both real runtimes, and
  `properties.replay` is `reproduced`, `not-reproduced` or `not-constructible`. It never changes
  a verdict (ADR 0035 decision 2). Needs M3-032, M3-003, M3-016. Added 2026-09-24. It is outside
  the 5% bar, which ranks precision tickets, and it makes a Divergent something a user can run.
- M4-010 (M) Seeded at scale: M0-012's mutation operators applied to corpus methods by a seeder
  in `tools/corpus/`, so seeded recall is measured over hundreds of seeds. A seed counts as a miss
  only when tests or replay confirm the behaviour changed. Needs M0-012. Added 2026-09-24.
- M4-007 (S) First full corpus run in the skill's `full` and `seeded` modes, plus a `full` run with
  `--execute`. It scores all five ADR 0028 criteria, including 100% recall on seeded behaviour
  changes, over the hand-written and mechanical seeds. Findings become tickets, not fixes. Needs
  M3-004, M3-022, M3-033, M4-001, M4-002, M4-004, M4-009, M4-010, P2-001.

Run observability (ADR 0038, accepted 2026-09-27). The M4-007 `full` run gave no sign of progress
after 2.5 hours. These tickets do not block M4-007 and change no verdict:
- M4-012 (L) `--verbosity quiet|normal|debug` and `--log`, a channel-backed writer that never
  blocks the pipeline, phase stopwatches, a heartbeat that names a stuck pair, and an IR-weighted
  ETA with a worst-case bound. Needs M3-004.
- M4-013 (M) Frontend sub-phases: projects loaded, procedures enumerated, pairs matched and lowered.
  Needs M4-012.
- M4-014 (S) Backend rung and solver timings at `debug`. Needs M4-012.
- M4-015 (S) Corpus runs log at `debug`, `corpus.ps1 -Progress`, and phase times in SUMMARY.md.
  Needs M4-012, M4-013, M4-014.

## P2 — Census findings (M3-022, M3-031)

Before the Git Extensions census is rerun: P2-011, then P2-010 and P2-012. P2-013 and P2-014
follow. P2-002 to P2-009 stay an unscheduled backlog: each unlocks under 5% of changed pairs in
the 2026-09-24 census. P2-001 cleared the bar and moved into the M4 list above, keeping its
ticket id (not listed again here). P2-015 is a `corpus.ps1` finding from the same run.

- P2-002 (S) `typeof(T)` as a shared synthesised input (reason `TypeOf`).
- P2-003 (S) `default(T)` lowered (reason `DefaultValue`).
- P2-004 (M) Event reads and event invocation (reason `EventReference`).
- P2-005 (M) Event `+=` and `-=` as calls to the accessors (reason `EventAssignment`).
- P2-006 (M) Assignment through a flow capture with no registered target (reason `FlowCaptureReference`).
- P2-007 (S) Find and lower the field assignments that stay opaque (reason `FieldReference`).
- P2-008 (M) `IIsNullOperation` from `?.` and `??` lowered through the null shadow (reason `IsNull`).
- P2-009 (S) A local written only by an opaque has a defined value (reason `undefined`).
- P2-010 (S) `IrLowerer.Destination` throws `KeyNotFoundException` on Git Extensions. Fixed; its corpus
  check moved to M3-031 criterion 7.
- P2-011 (M) A procedure whose lowering throws is reported and skipped, not the run.
- P2-012 (S) NuGet warnings NU1701, NU1702 and NU1903 are not project load failures.
- P2-013 (S) Projects outside the solution's build configuration are not loaded or counted.
- P2-014 (M) `corpus.ps1` prepares a box: long paths, submodules, isolation from this repo's
  MSBuild files, reference assemblies and the SDK resolver.
- P2-015 (S) `corpus.ps1 -Packages`'s `Get-ResolvedPackages` uses `Get-ChildItem -Recurse -Include
  'project.assets.json','packages.config'` on Windows PowerShell 5.1, where `-Include` without a
  wildcard `-Path` is not applied to the recursive walk: it visits every file (observed 3271 files
  under one repo against 48 real matches) and throws parsing a non-JSON one. Found in M3-031
  (2026-09-24); `-Packages` produced no data for any of the four pairs.
- P2-016 (M) `pmb-tomasjohansson__adapters-shortest-paths-dotnet`'s modern side loads 0 procedures
  (320 unverified) and the legacy side loads only 5, although `projectsSkipped` is legacy 6 /
  modern 0; the 6 legacy test projects fail restore with "doesn't list 'win' as a
  RuntimeIdentifier". Found in M3-031 (2026-09-24); M3-022 reported 100% load rate and 312
  matched pairs for the same repo.
- P2-017 (S) A dereference is null-checked where the CLR checks it, after the value, index or
  arguments, not before them (a false-Equivalent path). Found in P1-006, not by a census. Needs
  P1-006.
- P2-018 (S) A loaded project that declares types but yields zero procedures is skipped with
  reason `no-procedures` and exits 4, instead of reporting a clean, empty side. Found from P2-016's
  data: ShortestPaths' modern side reported 100% loaded with 0 procedures. P2-016 finds that
  repo's cause; this is the general guard. Needs M3-024.
- P2-019 (S) An array's length is never negative in a model. Found by M0-012's gate at the
  nightly budget (a false Divergent, rule 2); the gate skips it until this lands. M3-003 needs
  it (M0-012 criterion 7). Needs M0-012.
- P2-020 (S) SDK warning NETSDK1086 (an explicit `FrameworkReference` the SDK already implies) is
  not a project load failure. Found in M3-028 on eshop-upgrade-assistant's modern side; the same
  class of bug as P2-012. Needs P2-012.
- P2-021 (S) ShortestPaths' 6 legacy test projects are skipped with "doesn't list 'win' as a
  RuntimeIdentifier" under `corpus.ps1 -Env`. Found in M3-031; split out of P2-016.
- P2-022 (M) Compound assignment and `++`/`--` on `float`, `double` and `decimal`, and user-defined
  compound and increment operators, apply M4-002's `IrPure` functions instead of being opaque
  (reasons `CompoundAssignment`, `Increment`, `Decrement`). Found in M4-002 (`business-layer`
  `Subtotal`'s `decimal +=`). Needs M4-002.
- P2-043 (S) A Spacer derivation's inputs never give an array a negative length. Found by M0-012's
  gate on PR #241 (rule 2, CsCheck seed `6rdKklVqtDVa`): P2-019's clamp covered only `ModelDecoder.Inputs`.

## P2 — First real run findings (M4-007)

Verdict **continue** (`docs/runs/2026-09-27-m4-007-verdict.md`). Order by value: P2-031 and P2-032
first (together 81 of Git Extensions' 92 pair-level crashes), then P2-039 and P2-040 (they make
`--execute` safe to leave running), then P2-037, P2-038 and P2-036 (wrong or unconfirmed verdicts),
then the rest. P2-023 to P2-030 are opaque reasons with no owner; P2-025 (80) and P2-027 (54) are
the only large ones.

- P2-023 (S) `&x` (reason `AddressOf`).
- P2-024 (S) `new { ... }` (reason `AnonymousObjectCreation`).
- P2-025 (M) `(a, b) = ...` (reason `DeconstructionAssignment`).
- P2-026 (M) LINQ query syntax (reason `TranslatedQuery`), and the `from`-clause cast crash.
- P2-027 (M) Tuple literals and element reads (reason `Tuple`).
- P2-028 (S) `new T()` (reason `TypeParameterObjectCreation`).
- P2-029 (S) Calls through `dynamic` stay opaque by design (reason `DynamicInvocation`).
- P2-030 (S) `sizeof` (reason `SizeOf`).
- P2-031 (M) Verifying crashes when `this` is typed at different points in the hierarchy.
- P2-032 (S) Verifying crashes on `T[]` against `T[]?`.
- P2-033 (M) Verifying crashes with "IrSortValue ... was not present in the dictionary".
- P2-034 (S) Lowering crashes with a bare `NullReferenceException` on two methods.
- P2-035 (S) The mechanical seeder crashes on the two larger corpus pairs. Needs M4-010.
- P2-036 (M) A behaviour-preserving rename is reported Divergent. Needs M4-010.
- P2-037 (M) A solver Divergent the real runtimes do not reproduce (`SetSsh`). Needs M4-009.
- P2-038 (M) Replay reports `not-reproduced` for EQ006 results it cannot observe. Needs M4-009.
- P2-039 (M) `compare --execute` hangs when a replay driver never answers. Needs M4-009, P1-008.
- P2-040 (S) `compare --execute` runs solution code in the caller's working directory. Needs M4-009,
  P1-008.
- P2-042 (S) A generic call's identity differs on a nullable-annotated type argument (found in
  P2-032).
- P2-044 (M) A replay driver's protocol stdout is shared with the code under test (found by
  P2-039's dumps). Needs P2-039.
- P2-045 (S) A field read or written through `base` lowers to IR that fails validation (found
  verifying P2-044 on the Tomas pair).

## P2 — Success assessment (2026-09-28)

The assessment of M4-007's results found three gaps. Every rate is stale, because 41 commits
landed since that run, among them the fixes for 92 Git Extensions crashes. Divergent precision has
never been measured. And code-cleanup commits are not measured at all, since the corpus holds only
migrations and `migration-prompt.md` forbids refactoring. Order: P2-046 first, because every other
ticket reads its run. Then P2-047 and P2-048/P2-049 (the two missing measurements), then the rate
work, P2-050, P1-019, P2-051 and P2-052.

- P2-046 (S) Second full corpus run on M4-007's four pairs. Adds a "since M4-007" table, mechanical
  seeds on Git Extensions, the Preserving Equivalent share, a top-abstractions histogram and a
  re-scored per-ticket unlock table. Done 2026-09-30: 0 pair-level crashes (92 before), unchanged share 91.6%, lowerable share 39.1%,
  Preserving Equivalent share 70.1%, verdict continue (`docs/runs/2026-09-30-full-verdict.md`).
- P2-047 (M) Divergent audit: hand-adjudicate a fixed sample of EQ002 and EQ006 and report Divergent
  precision. Needs P2-046. Done 2026-09-30: 77 results, 2 confirmed, 50 false positive, 25
  undetermined, Divergent precision 3.8% (`docs/runs/2026-09-30-divergent-audit.md`, as corrected by
  P2-072); seven causes filed as P2-068 to P2-071 and P2-073 to P2-075.
- P2-048 (M) Five cleanup refactorings as Preserving seed operators, and a "cleanup proof rate" per
  seeded run. Needs M4-010, P2-035.
- P2-049 (M) Samples for cleanup refactorings (modern syntax, extract and inline method). Each
  non-Equivalent verdict becomes a ticket. Done 2026-10-02: of 9 behaviour-preserving pairs in
  `cleanup-modern-syntax` and `cleanup-extract-method`, 3 are Equivalent, 3 Unknown and 3 Divergent.
  Filed as P2-093 to P2-097, the two precision bugs first:
  - P2-097 (L) Precision bug: extracting or inlining a private helper makes its caller Divergent,
    on the call trace alone. An `equiv-adr` decision on callees that exist on one side only.
  - P2-094 (M) Precision bug: `string.Format` with plain holes is Divergent from the interpolated
    string it becomes.
  - P2-093 (S) A relational pattern (`>= 90`) lowers as a comparison, not opaque `switch-pattern`.
  - P2-095 (M) A conversion to `Nullable<T>` and `default(T?)` lower, so `x == null ? (int?)null :
    x.Length` and `x?.Length` are compared.
  - P2-096 (L) A filter loop against `Where(...).ToList()` is Unknown(abstraction). An `equiv-adr`
    decision on modelling LINQ-to-objects operators.
- P2-050 (M) Deterministic solver budgets (Z3 `rlimit`, wall-clock as a backstop), and the
  timeout Unknowns measured at 1x, 4x and 20x. Needs P2-046. Done 2026-10-01: `resourceLimit`
  5,000,000 with `timeoutMs` 60,000 behind it. Of 172 timeouts, 4x decides 37 and 20x decides 73,
  none Equivalent (`docs/runs/2026-10-01-timeout-budget.md`); P2-100 and P2-101 filed.
- P1-019 (M) Spike: how many `abstraction` Unknowns (261 of 700 on Git Extensions) refinement would
  resolve. An ADR only if the answer is at least 5%. Needs P2-046.
- P2-051 (M) `runtime-diff` covers Windows Forms and `System.Drawing`, and every external callee.
  Needs M3-033.
- P2-052 (M) Replay and differential testing reach `internal` methods (via `InternalsVisibleTo` on the
  emitted compilation). Needs M4-009, P1-008.
- P2-059 (S) Soundness: rung 4 is Equivalent only when Spacer's invariant solves the clauses (Z3 5.1's
  Spacer proved a pair that rungs 1 and 3 refute, CsCheck seed `4FfExD8adOs4`). Needs P1-001.
- P2-060 (M) A call writes only the heap its callee can reach, so `String.Concat` cannot change a
  user field (a false Divergent P1-017's IL gate found). Needs P1-017.
- P2-081 (M) A Divergent that rests on a closed BCL call's `threw` flag or result, one the real member
  cannot give, is not EQ002 (the gate's nightly budget, found by P2-060). Needs P2-060.
- P2-080 (S) The differential gate draws the same pairs on every pull request: CsCheck's `seed` fixes
  the first pair only, so 199 of the 200 are random and `ABrokenIlMappingIsCaught` failed on `main`. Needs P1-017.

### Runtimes are detected, not assumed (ADR 0040)

`equiv` supports a framework migration, a version upgrade (net6 to net8) and a same-runtime commit
(a cleanup), and reads each project's runtime instead of assuming 4.8 and 10. P2-049's cleanup samples are same-runtime pairs, so they wait for P2-055.

- P2-053 (M) Detect each project's runtime (the `TargetFrameworkAttribute`, `netstandard` resolved
  to its hosts, a `runtimes` config key) and report it in `run.properties.runtimes`.
- P2-054 (M) Every `runtime-changes.json` row gets `changedIn`, and `RuntimeChangeTable` matches by
  runtime interval. Needs P2-053.
- P2-055 (L) Runtime rules (table rows, float-to-int saturation, x87) apply only inside a pair's
  interval. New samples `same-runtime-cleanup` and `version-bump`. Needs P2-053, P2-054.
- P2-056 (M) `--execute` and `runtime-diff` run each side on its detected runtime, and need Windows
  only for .NET Framework. Needs P2-053.
- P2-057 (S) `--before`/`--after` aliases, and scope wording in README and specs. Needs P2-055, P2-056.
- P2-058 (M) Pin three public "no functional change" PRs as `cleanup` corpus pairs (Git Extensions
  #11372 and #11284, PowerShell #19687), run them, and adjudicate every Divergent. Needs P2-055,
  P2-047. Done 2026-10-02: no cleanup changed behaviour. Of 575 changed pairs (counting
  PowerShell's 12 edited pairs, not its 128 identical opaque ones), the solver proved 57 (9.9%),
  52 are Divergent and 466 Unknown. 51 of the 52 Divergent are
  false positives and 1 is a commit-hash constant that differs between any two commits
  (`docs/runs/2026-10-02-cleanup-verdict.md`). Filed as P2-098, P2-099 and P2-103 to P2-107.

Found by P2-058's cleanup runs (`docs/runs/2026-10-02-cleanup-verdict.md`). A cleanup pair keeps
the runtime, so every one of these is about the engine and none is about a runtime rule. In order
of how much of the cleanup result each explains:
- P2-098 (M) A constant that only says where or from which commit the code was built
  (`[CallerFilePath]`, a generated commit hash) is not a divergence. 45 of the 52 Divergent, and it
  keeps unedited bodies from being congruent. Starts with `equiv-adr`.
- P2-099 (L) A collection expression equals the `new` and initializer it replaces. It is an opaque
  `Conversion` in 308 of gitextensions-11372's 351 changed pairs, and behind 2 false Divergent.
  Done 2026-10-03: the solver proves 160 of the 351 (45.6%, from 2.0%); the 2 Divergent remain
  (`docs/runs/2026-10-03-cleanup-gitextensions-11372/SUMMARY.md`).
- P2-120 (L) A collection expression whose elements are evaluated ahead of it, and the targets
  P2-099 left opaque: 3 false Divergent and 104 changed pairs on gitextensions-11372. Needs P2-071.
- P2-103 (M) `x as T` followed by a null check equals `x is T t`. All 5 of powershell-19687's
  Divergent, and none of its 12 edited pairs is proved.
- P2-104 (S) `x is not T t` lowers as the negation of its inner pattern, not as an opaque.
- P2-105 (M) Five procedures make the lowerer throw a null reference, so all three runs exit 5.
- P2-110 (M) Rerun the three cleanup pairs on one commit once the crashes are fixed, and write the
  cleanup verdict again. Needs P2-105.
- P2-106 (M) A body whose only diagnostics are warnings promoted to errors is not `unbound`. 82
  pairs on powershell-19687. Starts with `equiv-adr`. Needs P2-085.
- P2-107 (M) On a same-runtime pair, a whole-body opaque pair with identical source is not
  "changed": 128 of powershell-19687's 140 changed pairs. Starts with `equiv-adr`.
- P2-067 (L) `DelegateCreation` is 17.0% of Git Extensions' changed pairs on its own and has no open
  owner: split it by cause, then lower the chosen construct. Found by P2-046. Done 2026-10-01: in
  134 of the 213 such pairs the lambda was unchanged and already shared; a lambda or method group
  whose conversion runs no code is now the pure function `delegate:<fingerprint>`, the reason alone
  fell to 68 changed pairs and the lowerable share rose from 40.3% to 51.5%. Verdicts are not
  expected to move until a delegate both sides apply stops being tainted (ADR 0026).
- P2-061 (M) Three Preserving mechanical seeds on Git Extensions turn Equivalent into Divergent; find
  the cause of each (seeder or engine). Found by P2-046.
- P2-062 (S) An opaque fragment in `properties.abstractions` names its reason. Found by P2-046.
- P2-063 (S) Corpus tooling: `seeds.json` records the mutated line, `-Metrics` reads `unknownReason`,
  and the skill warns that overlapping runs on one checkout are void. Found by P2-046.

The 2026-09-30 goal review measured the backlog against two product goals: prove an in-place
migration (framework to Core, or a version bump) equivalent and narrow what is left for a human, and
prove or refute a small cleanup. It found three gaps that no ticket owned:
- about 1,085 flagged results on Git Extensions, in no order;
- one real migration behind every judgement;
- no version upgrade in the corpus.

- P2-064 (L) Every run ends with a short review list: flagged results grouped by cause and ranked
  (SARIF `rank`, `run.properties.reviewList`, stdout). Needs P2-062.
- P2-065 (M) Full runs of the five migration pairs never run (Duplicati, OpenRA, and the three eShop
  pairs, two of them migration-tool output), reported against Git Extensions. Done 2026-10-01: none
  of the five finished clean. Duplicati and OpenRA crash with no SARIF, eshop-manual completes with 3
  crashed pairs and proves 0 of 26 changed pairs, and each tool pair compares 2 procedures because
  the tool's output does not compile (`docs/runs/2026-10-01-migrations-verdict.md`). Filed as P2-082
  to P2-088.
- P2-066 (M) Pin two public .NET-to-.NET version upgrades (a pure bump, and a bump with fixes) and run
  them; every EQ006 must cite a row inside the pair's interval. Needs P2-055, P2-056, P2-047.

Found by P2-065's runs (`docs/runs/2026-10-01-migrations-verdict.md`). Git Extensions is the only
real migration `equiv` gets through, so the first two come before any rate work, and Duplicati and
OpenRA are rerun once they land:
- P2-082 (S) First: weighing a pair for the progress log can no longer end the run. One lowered
  procedure makes `IrLoopAnalysis` throw outside the per-pair `try`, so Duplicati and OpenRA exit 5
  with no SARIF.
- P2-083 (S) Lowering a binary operator in a branch condition no longer throws a bare
  `NullReferenceException`: 19 pairs over eshop-manual, OpenRA and Duplicati.
- P2-085 (L) A modern project that does not compile is still compared, method by method, with each
  erroneous method Unknown(unbound). Done 2026-10-01, under clarifications of ADR 0029 and ADR 0028:
  `eshop-upgrade-assistant` goes from 2 matched pairs to 135 (101 Equivalent by congruence, 17
  unbound). `eshop-porting-assistant` is unchanged, because its project cannot be opened at all
  (P2-091).
- P2-091 (M) Porting Assistant's output is still skipped: the `Microsoft.Net.Compilers` package it
  leaves replaces the SDK's `Csc` task, which MSBuildWorkspace reports as a failure, and its restore
  fails on NU1605. Find out whether the compilation the workspace returns is the project's. Needs
  P2-085.
- P2-084 (S) A project whose only types are empty is not a load failure (Duplicati's placeholder
  project; load rate 98.1% on a human pair).
- P2-087 (M) `Binary` (lifted operators and the rest) alone is 6.2% of OpenRA's changed pairs and has
  no open owner since P1-018 left the IL fallback off. Needs P2-083.
- P2-086 (M) `InterpolatedString` alone is 19.2% of eshop-manual's changed pairs, same reason: the
  same text binds differently on the two runtimes. Done 2026-10-01: a string of `string` and integer
  holes lowers as the concatenation of its parts under both bindings; the reason alone fell from 5 to
  2 pairs on eshop-manual (7.7%), 25 to 2 on Duplicati and 45 to 7 on Git Extensions.
- P2-102 (S) The 2 eshop-manual pairs left: an integer hole followed by a hole that runs code, which
  the two bindings format in a different order. Covering it assumes a hole does not change the
  current culture. Starts with `equiv-adr`'s bar test. Needs P2-086.
- P2-088 (S) `AnonymousObjectCreation` alone is 7.7% of eshop-manual's changed pairs (2 of 26): decide
  whether it stays opaque. Done 2026-10-02: 44 of 47 measured sites pass the object straight to a call,
  and that case lowers as a closed call of the property values; changed pairs holding the reason fell
  from 2 to 0 on eshop-manual and 24 to 1 on Duplicati.
- P2-090 (S) A branch on a compile-time constant lowers to a jump along its live edge, not to a
  branch into a block with no terminator. Seen while probing for P2-083; a likely cause of P2-082's
  `IrLoopAnalysis` crash, to be confirmed there.
- P2-092 (S) The census counts a body that is whole-body opaque for several causes. Found by P2-085's
  run: an unbound body has one opaque per error, so `pairsWholeBodyOpaque` held 6 of
  eshop-upgrade-assistant's 17 unbound pairs, the ones with a single error. Done 2026-10-01: the
  census reads `IrOpaque.WholeBody`, so a body of nothing but flagged opaques counts however many.

P2-047's audit found Divergent precision of 3.8% (2 of 52 adjudicated). Each false-positive cause is
one ticket, in order of how many false positives it accounts for. Four causes (P2-068 to P2-071)
give one call two identities. Three (P2-073 to P2-075) are EQ006 rows that match on the member alone.

- P2-073 (M) An EQ006 row fires only when the call's constant arguments can reach the change
  (regex without case-insensitive ranges, constant paths and formats). 15 false positives.
- P2-068 (M) A call to a one-line forwarder is the same call as its BCL target. 11.
- P2-069 (L) An unchanged call site that a dependency upgrade rebinds (class to interface, generic
  instantiation, namespace move) is Unknown, not Divergent. 9.
- P2-074 (M) A path row does not fire on a path from a valid-path source or behind a validating
  guard. Needs P2-073. 7.
- P2-070 (M) Identical source that binds to a different BCL overload is the same call (table
  entries). 3.
- P2-071 (M) An effect-free BCL call (pure getter, empty-collection constructor) is not a trace
  event. 3.
- P2-075 (S) The ICU and `ListViewGroup` rows match only the members their change affects. 2.
- P2-072 (M) Two identical bodies with a rethrowing catch lower to different call traces. Done
  2026-10-02: not a bug. The two bodies are different source (the modern solution compiles an edited
  copy), the Divergent is right, and the audit's row 17 is corrected to undetermined.

Run time (found 2026-09-30 during P1-018). A Git Extensions `full` run takes 8h57m. That is over a
hosted GitHub runner's 6-hour job limit, and six pairs that all end Unknown spend 5.4 of its 6.8 verify
hours. Neither ticket may end a pair early or change a decided verdict, and neither adds a cap on a
rung, a pair or a run.
- P2-076 (M) Measure and remove the time a pair spends outside its solver budget: one rung reported a
  5000 ms timeout after 126.5 minutes. Non-solver work is made cheaper, never skipped. The contracts
  pass (2h07m, unlogged) becomes a phase. Two full runs must agree on every decided result.
- P2-077 (M) Matched pairs are verified in parallel (`--jobs`), with the same results as one at a time
  and no query ended sooner by contention. Needs P2-050, P2-076.
- P2-112 (S) An interrupt that throws on the timer thread can no longer end the process.
  `Context.Interrupt()` threw `Z3Exception: canceled` on P2-076's timer and killed a CI test host
  (PR #368); the same crash would end a corpus run with no SARIF.
- P2-109 (S) Unrolling a loop is linear in its size. One OpenRA pair, a loop that calls its own procedure,
  spent over twenty minutes in `IrUnroller` before its first solver query (found by P2-082's run). Needs P2-076.
- P2-113 (M) A pair whose unrolled body is huge overflows the native stack in the trace encoding and ends the run
  with no SARIF, which no per-pair `try` can catch. Same OpenRA pair, reached once P2-109 let it finish unrolling.
  Needs P2-109.

Found by P2-072's repro:
- P2-108 (M) `System.IntPtr` and `nint` (and `System.UIntPtr` and `nuint`) are one type in an
  identity on every target. A byte-identical method with an `IntPtr` parameter is one EQ005 and one
  EQ004 between net48 and net10.0. The bodies' verdict is recorded, not fixed.

Found by P2-050's measurement (`docs/runs/2026-10-01-timeout-budget.md`):
- P2-100 (M) The same query under the same resource limit ends the same way whenever the garbage
  collector runs. Two runs at the default budget agreed on 179 of 184 pairs; with collections made
  rare the pairs that differed repeat. Needs P2-050.
- P2-101 (M) Measurement: what the 99 timeouts that 20 times the budget does not decide have in
  common, and whether another tactic pipeline proves any. Needs P2-050, P2-076.

Found by P1-018's run (`docs/runs/2026-10-01-il-fallback-verdicts.md`):
- P2-079 (M) Soundness, first: the IL lowering shares an opaque that names a lambda or local function
  without its body, and lowers a runtime-changed method group as a plain constant, so two different
  lambdas prove Equivalent (repro in the ticket). 20 of P1-018's 21 new Equivalents rest on it.
  Until it lands, no `--il-fallback` verdict on a method that holds a lambda is to be relied on.
  Needs P1-016, P1-017.
- P2-078 (M) Ill-sorted IR from the IL lowering no longer crashes the encoder: one Git Extensions pair
  exits the `--il-fallback` run with code 5. `IlFallback` keeps the IOperation bodies when the IL
  bodies do not validate. Needs P1-016, P1-017.

Documentation:
- P2-089 (S) ARCHITECTURE.md's `equiv compare` synopsis and `EquivConfig`'s doc comment no longer list
  `--bound` and `--timeout-ms`, which the command never had (`bound` and `timeoutMs` come from the
  config file and the MCP tool). No option is added. Needs P2-050.

## M5 — Agent surface (MCP)

Coding agents do migrations; M5 lets them check their own work while they do it (ADR 0033). It
needs only a shippable binary, so it can run alongside M4.

- M5-001 (M) `equiv mcp`: an MCP server over stdio in the same binary and container, with
  `compare` and `lower_only` tools that return the SARIF log. Needs M3-004. A remote (HTTP)
  transport waits for the hosted tier.
- M5-002 (M) A `probe` tool: the agent names a matched pair and supplies arguments, and gets both
  runtimes' outcomes. It is never a verdict, and it is registered only on Windows (ADRs 0035 and
  0036). Needs M5-001, M4-009.

## M6 — Hosted tier on Container Apps

The hosted tier runs the release image as Azure Container Apps Jobs (ADR 0032). M6 proves the
shape on a real subscription inside the Consumption plan's monthly free grant, before any API,
keys or quotas exist. It can run alongside M4 and M5.

- M6-001 (M, done: PR #248) Bicep deployment of a Container Apps environment and a manually started job that
  runs `equiv compare` on the samples from an Azure Files share, a $5 budget alert, and a teardown
  script. Needs M3-004 (which needs M3-029, so the Linux image can load solutions).
- Later, unwritten until M6-001 lands: queue-triggered executions, blob inputs, an HTTP API with
  keys and quotas, and `equiv mcp` over Streamable HTTP (ADR 0033).
- No execution oracle in the hosted tier. ADR 0035's `--execute` needs Windows and .NET Framework
  4.8, and Container Apps runs Linux containers only. Hosted results therefore never carry
  `replay` or `differentialTesting`. Revisit only if a Windows compute option is chosen by a new
  ADR, which would also need a sandboxing decision, because `--execute` runs customer code.

## P1 — Beyond the solver: loop ladder rungs 4 and 5, and the second oracle (first post-MVP milestone, tickets written)

- P1-001 (L) Constrained Horn clause encoding solved by Z3 Spacer for non-aligned loops.
- P1-002 (M) LLM-proposed coupling invariants, Z3-checked; pluggable model, off by default.
- P1-003 (M) (promoted into M3) Split `IrLowerer` into heap and exception collaborators, with the swapped block-map
  state as an explicit `LoweringContext` parameter (PR #30 review; behaviour-preserving).
- P1-004 (M) `foreach` over an array as an index loop (M2-004 size guard; Roslyn's CFG desugars every `foreach` into the enumerator pattern). Needs M4-001, and P1-003 first: this ticket adds to both paths it extracts.
- P1-005 (L) (promoted into M3) An `IrCall` reads and writes the heap: its effects and
  results are functions of the callee, its arguments, the heap at the call and its position,
  shared by both sides (ADRs 0015, 0018).
- P1-006 (L) (promoted into M3) Array element and length maps keyed by the array value instead of the array
  variable, so two variables holding one array are one slice (ADR 0015).
- P1-007 (M) Rung 1 inlines self-calls that read or write the heap. The self-call's heap pairs thread the heap
  through the inlined copy. This removes the array-variable, synthesised-`Ref` and heap-write obstacles, which
  are stale after M3-007 and P1-006. It needs P1-005 and P1-006, and M3-003 does not need it (it is precision,
  not soundness).
- P1-008 (L) Tested Unknowns: with `--execute`, a constructible Unknown pair runs on generated
  inputs until the Good-Turing discovery probability (Böhme et al., FSE 2021) falls below a target.
  It stays EQ003 with `properties.differentialTesting`. An observed divergence is EQ002 with
  `proofMethod: observed` (ADR 0035 decision 3). Needs M4-009. This is the first post-MVP ticket:
  it gives the stated likelihood figure for code the solver cannot decide.
- P1-009 (M) Trace-mined coupling invariants: `IrInterpreter` traces propose, P1-002's rung checks.
  On by default because nothing leaves the machine (ADR 0036). Needs P1-002, P1-008.
- P1-010 (L) Caller-sufficient relational callee contracts. A changed callee that the caller
  cannot observe moves from `unprovenAssumptions` to `contractsUsed` (ADR 0036 decision 2). Needs
  M3-015, P1-005, P1-002.
- P1-011 (M) Spike: would equality saturation (Peggy, egg) close changed pairs that congruence and
  Z3 cannot? It measures on Git Extensions and writes an ADR only if the share is at least 5% of
  changed pairs. Needs M3-015, M4-004.
- P1-012 (M) Spike: how much of the opaque tail disappears if the fallback lowers from IL
  (ICSharpCode.Decompiler's ILAst)? It measures, and writes an ADR against ADR 0003 only if the
  gain is at least 5% and compiler shape drift is small. Needs M4-001, M4-002, M4-004, P2-001. Done 2026-09-28: 10.0% lowerable, 1.7% drift; wrote ADR 0039.
- P1-013 (M) Failure refinement: every Unknown reports whether the modern side can newly fail,
  and whether it removed a failure (ADR 0037). Needs M3-016, M3-025.

IL fallback lowering (ADR 0039, accepted 2026-09-28). P1-012 measured 114 of Git Extensions' 1,143
changed pairs (10.0%) lowerable from ILSpy's ILAst with no unmapped instruction, and shape drift in
19 (1.7%) (`docs/runs/2026-09-28-il-lowering-spike.md`). No single construct ticket for that tail
clears the 5% bar. The fallback ships off by default until P1-018 measures verdicts, not lowerability:
- P1-014 (L) `ICSharpCode.Decompiler` becomes a product dependency of `Equiv.Frontend.CSharp`. It
  reads a method's ILAst and lowers control flow, integral arithmetic and calls, with identities
  resolved through the loaded compilation, plus an IL lowering oracle and `IL-COVERAGE.md`. Needs
  P1-012, M4-009.
- P1-015 (L) The rest of the spike's table: fields, arrays, addresses, type tests, exceptions and
  pure operators, through `IrLowerer`'s own collaborators. Needs P1-014.
- P1-016 (M) `--il-fallback`: the per-pair rule in a run, `properties.lowering`, census counts and
  `samples/il-fallback`. Needs P1-015.
- P1-017 (M) M0-012's differential gate also verifies every pair through the IL lowering. Needs P1-016.
- P1-018 (S) Git Extensions with and without the fallback. On by default only if the pairs it moves
  to a decided verdict are at least 5% of changed pairs and no Equivalent regresses. Needs P1-016,
  P1-017. Done 2026-10-01: 21 of 1,294 changed pairs (1.6%), so the default stays off
  (`docs/runs/2026-10-01-il-fallback-verdicts.md`); one IL-lowered pair crashes the encoder (P2-078).
  A hand check found 20 of the 21 new Equivalents unproved, from a soundness bug (P2-079).

Order after M4-007: P1-008 first. Then P1-013 and the two spikes (cheap, and they decide their
own futures). Then P1-001 → P1-002 → P1-009, and P1-010. P1-012 wrote ADR 0039, so P1-014 →
P1-015 → P1-016 → P1-017 → P1-018 follow it, in parallel with the loop ladder tickets (they
share no files).

P1-005 and P1-006 are the two soundness limits ADR 0015 names. ADR 0018 moves both ahead
of M3-003, so no build that reports sample verdicts carries them. Until they land, an
Equivalent verdict on a procedure that writes a field around a call, or that takes two
array parameters, rests on an assumption no gate can see; M3-001's soundness harness runs
over IR and does not cover them.

## Post-MVP (unordered backlog, separate tickets when scheduled)

Moved from M4 by the 2026-09-24 census (`docs/runs/2026-09-24-census-verdict.md`): each unlocks
under ADR 0028's 5% bar (now 5% of Git Extensions' 315 changed pairs = 16). Ticket files and ids
are unchanged; a later census that shows more changed pairs can move any of these back into M4.

- M4-003 (M) `ref`/`out` call arguments and `lock`. 8 changed pairs (2.5%). Needs P1-005. `lock` split out
  to M4-011.
- M4-011 (S) `lock` through its `try`/`finally`: the CFG's `lockTaken` local starts at `false`. Needs M4-003.
- M4-005 (M) Type tests and downcasts. 13 changed pairs (4.1%). Needs M3-010.
- M4-006 (M) `await` as a call. 9 changed pairs (2.9%). Needs M4-001.
- M4-008 (M) The remaining whole-body opaques: arrow-bodied and auto-property accessors, `catch`
  filters and bare `catch` (ADR 0029). 3 changed pairs (1.0%). Needs P1-003, M3-010, M3-025.

- Floating point as IEEE sorts; `decimal`; string theory for common `string` ops.
- Boogie backend behind `IVerificationBackend` for invariant-driven unbounded loops.
- Java frontend (Eclipse JDT sidecar) reusing Core, Verify, Cli unchanged.
- Web UI: SARIF viewer + CFG split pane (React Flow). Only after users ask.
- SonarQube: confirm `sonar.sarifReportPaths` ingestion of EQ* rules; GitHub Code Scanning upload step in `action.yml`.
- `--il-fallback` on by default (ADR 0039, P1-018): measured on Git Extensions 2026-10-01. Of 1,294 changed pairs it lowers 116 from IL and moves 21 (1.6%) from Unknown(opaque) to Equivalent and 21 to Divergent, none reproduced by replay; below ADR 0028's 5% bar, with no Equivalent regressed, one crash (P2-078) and one Divergent turned Unknown(timeout). A hand check found 20 of the 21 Equivalents unproved: the IL lowering never reads a lambda's body (soundness, P2-079), so the sound gain is 1 pair (0.1%). The option stays, off by default (`docs/runs/2026-10-01-il-fallback-verdicts.md`).
- Congruence modulo verified rewrites (equality saturation, P1-011): measured on Git Extensions 2026-09-28, 0 of 1,195 changed pairs (0.0%) close under the rule set, below ADR 0028's 5% bar; not scheduled (`docs/runs/2026-09-28-egraph-spike.md`).

From the 2026-09-24 second-oracle review, unticketed until a result above asks for them:
- Shadowing the residual in staging or production: generate a Scientist.NET experiment, or a
  Diffy configuration per routed endpoint, for each remaining Unknown. This is a new output
  surface beyond SARIF, so it needs an ADR against ADR 0006.
- Coverage-guided generation for P1-008 (SharpFuzz): an ADR 0002 row, once plain generation's
  discovery probability visibly plateaus.
- A second solver (cvc5, BSD-3-Clause, as a separate process) cross-checking Equivalent
  verdicts, with Alethe proof certificates checked by Carcara. Low priority: M0-012 and M4-009
  catch encoding bugs, which are far likelier than solver bugs.
- Stronger string solving (cvc5 strings, OSTRICH) for string-heavy line-of-business code, if
  M4-007 shows string opaques dominating the residual.
- ARDiff-style refinement (Badihi et al., FSE 2020): start with unchanged fragments as
  uninterpreted functions and refine only on spurious counterexamples. It extends ADR 0024's shared
  fragments.
  Measured by P1-019 on Git Extensions 2026-09-30: interpreting the only closed-form `IrPure` kinds
  (`IntPtr ==`, `!=`) resolves 7 of 726 Unknowns (1.0%), all to Divergent. That is below ADR 0028's
  5% bar, so it is not scheduled. 195 of the 238 `abstraction` Unknowns hold an opaque fragment, and
  the other 36 need floating point, `string` or operator bodies (`docs/runs/2026-09-30-abstraction-spike.md`).
- Abstract semantic differencing (Partush and Yahav, OOPSLA 2014): relational abstract domains
  for loops where the ladder and CHC time out.
- Partition verdicts (PASDA, Glock et al., JSS 2024): Equivalent on some input partitions and
  Divergent on others. ADR 0037 rejected this for now.
