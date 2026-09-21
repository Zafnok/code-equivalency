# Quality gates

All gates run locally via `./build.ps1` (M0-002) and in GitHub Actions on every PR.
A PR cannot merge unless every gate is green. Versions are pinned in
`Directory.Packages.props` (Central Package Management) and listed in
[adr/0002-dependencies.md](adr/0002-dependencies.md).

| Gate | Tool | Setting | Blocking |
|---|---|---|---|
| Build | .NET 10 SDK | `TreatWarningsAsErrors`, `Nullable=enable`, `AnalysisLevel=latest-all`, `EnforceCodeStyleInBuild`, `Deterministic`, `ContinuousIntegrationBuild` in CI | yes |
| Analyzers | Microsoft.CodeAnalysis.NetAnalyzers (built in), Meziantou.Analyzer | all rules on; severities only lowered in `.editorconfig` with a comment | yes |
| Format | `dotnet format --verify-no-changes` | `.editorconfig` is the single style source | yes |
| Unit/property/snapshot tests | xUnit v3 on Microsoft.Testing.Platform (`dotnet.config` `[dotnet.test:runner] name="Microsoft.Testing.Platform"`), CsCheck, Verify | | yes |
| Coverage | coverlet.MTP → cobertura → `tools/check-coverage` (M0-003) | 100% line and branch per `src/` project; coverlet.MTP has no threshold flag, so the check is a small script over the cobertura XML | yes |
| Architecture | ArchUnitNET (xUnit v3 package) | dependency edges from ARCHITECTURE.md, naming rules from CLAUDE.md | yes |
| Integration | `Equiv.Tests.Integration` runs the CLI on every `samples/*` and compares SARIF snapshot | Windows runner only (needs VS Build Tools) | yes |
| Mutation | Stryker.NET 5 (`--test-runner mtp`) | `--break-at 90` per `src/` project, raised per milestone; blocking since M0-011. PRs run incrementally (`--since` the base branch, M0-007), so the score a PR is held to is the score of the files it changed; the nightly schedule is a full sweep. A project or PR with no mutants has no score and passes | yes |
| Code smells / duplication | SonarQube Cloud | Sonar "Sonar way" Quality Gate on new code (duplication, maintainability/reliability/security ratings); `continue-on-error` until calibrated against a few real PRs, then promoted (ADR 0009) | later |
| Code smell backlog | `tools/sonar-triage` | Sonar's *overall* findings, which the new-code gate never sees, batched into GitHub issues labelled `sonar` by `sonar-triage.yml` (weekly + manual). Filing only; the fixes are ordinary PRs (ADR 0016) | no (reporting) |
| Security | GitHub CodeQL (C#), `dotnet list package --vulnerable --include-transitive` fails on any | | yes |
| Dependency licence | `tools/licence-check` (M0-010), wraps the `nuget-license` local tool | every package in every `packages.lock.json`, `.config/dotnet-tools.json` and samples/ direct reference must resolve to a licence on `tools/licence-check/policy.json`'s allowlist or a reasoned exception in it (ADR 0017); also regenerates `THIRD-PARTY-NOTICES.md` and fails if that changes the tracked file | yes |
| Secrets | gitleaks action | | yes |
| Supply chain | Dependabot weekly, NuGet lock files (`RestorePackagesWithLockFile`), `--locked-mode` in CI | | yes |
| Versioning | MinVer from git tags | | n/a |
| Packaging | `dotnet publish` single-file for win-x64 + linux-x64, Docker multi-stage image (`equiv:<version>`), GitHub Action wrapper `action.yml` | M3 | yes from M3 |

## CI matrix

- `windows-latest`: full pipeline including integration tests (VS Build Tools present on
  hosted runners; the 4.8 targeting pack ships with VS).
- `ubuntu-latest`: build, unit/property/snapshot, architecture, coverage, CodeQL. Integration
  tests are skipped until the bare loader exists (post-MVP).

## Required checks (M0-004)

CI runs in `.github/workflows/`: `ci.yml` (gates on windows-latest + ubuntu-latest, plus
`vulnerable-packages` and `gitleaks`), `codeql.yml`, `mutation.yml` (blocking since M0-011;
see Mutation row above), `sonar.yml` (M0-006, informational; see Code smells row above).
Applying branch protection with these as required checks on GitHub
is the user's action — this ticket only wires the workflows. Mark as required:

- `gates (windows-latest)`
- `gates (ubuntu-latest)`
- `vulnerable-packages`
- `gitleaks`
- `analyze` (CodeQL)
- `stryker (Equiv.Core, Equiv.Core.Tests)`, `stryker (Equiv.Cli, Equiv.Cli.Tests)`,
  `stryker (Equiv.Frontend.CSharp, Equiv.Frontend.CSharp.Tests)`,
  `stryker (Equiv.Verify.Z3, Equiv.Verify.Z3.Tests)` (M0-011; one check per matrix leg, so a
  new `src/` project needs its own entry here and in the ruleset)

Do not mark `sonar` (sonar.yml) as required until it has been calibrated per ADR 0009.

## Test taxonomy (what "a variety of tests" means here)

1. **Unit** — one behaviour, no I/O. Required for everything.
2. **Property** (CsCheck) — invariants over generated inputs. Required for IR, encoding,
   matching, baseline fingerprinting.
3. **Snapshot** (Verify) — IR dumps, SARIF output. Required for every sample and every
   lowering rule. Snapshots are reviewed in the PR like code.
4. **Integration** — CLI on `samples/`. Required per milestone.
5. **Architecture** — ArchUnitNET. Required once; extended when a rule is added.
6. **Mutation** — Stryker. Score reported per PR; blocking below 90 since M0-011.
7. **Benchmark** (BenchmarkDotNet) — optional; only when a ticket is about performance.

## Coverage exclusions policy

`[ExcludeFromCodeCoverage(Justification = "M?-???: reason")]` only on: generated code,
process entry points that exec the real solver/MSBuild and are covered by integration
tests instead. Nothing else. The coverage script fails if it finds the attribute
without a ticket id in the justification.
