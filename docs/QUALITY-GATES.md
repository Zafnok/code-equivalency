# Quality gates

All gates can run locally via `./build.ps1` (M0-002), and run in GitHub Actions on every
PR (`.github/workflows/ci.yml`, `mutation.yml`) exercising the identical commands.
A PR cannot merge unless every gate is green. Default to trusting the CI run rather than
also running the full gate locally before every commit — see
`.claude/skills/equiv-quality-gates` for when a local run is actually worth it. Versions
are pinned in `Directory.Packages.props` (Central Package Management) and listed in
[adr/0002-dependencies.md](adr/0002-dependencies.md).

| Gate | Tool | Setting | Blocking |
|---|---|---|---|
| Build | .NET 10 SDK | `TreatWarningsAsErrors`, `Nullable=enable`, `AnalysisLevel=latest-all`, `EnforceCodeStyleInBuild`, `Deterministic`, `ContinuousIntegrationBuild` in CI | yes |
| Analyzers | Microsoft.CodeAnalysis.NetAnalyzers (built in), Meziantou.Analyzer | all rules on; severities only lowered in `.editorconfig` with a comment | yes |
| Format | `dotnet format --verify-no-changes` | `.editorconfig` is the single style source | yes |
| Unit/property/snapshot tests | xUnit v3 on Microsoft.Testing.Platform (`dotnet.config` `[dotnet.test:runner] name="Microsoft.Testing.Platform"`), CsCheck, Verify | includes the differential soundness gate (`DifferentialSoundnessTests`, VERIFICATION-MODEL.md section 7, M0-012): 200 generated C# pairs per PR in the Windows `gates` leg, which runs `Equiv.Tests.Integration`, and 5,000 in `mutation.yml`'s nightly `differential` job (`EQUIV_DIFFERENTIAL_BUDGET=nightly`; `EQUIV_DIFFERENTIAL_SEED` replays a failure's seed) | yes |
| Coverage | coverlet.MTP → cobertura → `tools/check-coverage` (M0-003) | 100% line and branch per `src/` project; coverlet.MTP has no threshold flag, so the check is a small script over the cobertura XML | yes |
| Architecture | ArchUnitNET (xUnit v3 package) | dependency edges from ARCHITECTURE.md, naming rules from CLAUDE.md | yes |
| Integration | `Equiv.Tests.Integration` runs the CLI on every `samples/*` and compares SARIF snapshot | Windows runner only (needs VS Build Tools) | yes |
| Mutation | Stryker.NET 5 (`--test-runner mtp`) | `--break-at 90` per `src/` project, raised per milestone; blocking since M0-011. PRs run incrementally (`--since` the base commit the PR's merge ref was built on, M0-007), so the score a PR is held to is the score of the `src/` files it changed, tested against the whole new test suite; test-side changes (`tests/**`) are ignored by the diff (`.github/stryker-pr-config.json`: under the MTP runner Stryker 5.0.0 cannot tell which tests a changed test file holds, so any test edit re-ran every mutant of the project). The nightly schedule is a full sweep and is what catches a test edit that lets a mutant in an unchanged file survive. A project or PR with no mutants has no score and passes; a PR leg whose project has no changed `.cs` file skips Stryker for that reason | yes |
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

## Reusing a pass on unchanged code

The `gates` legs (ci.yml) and `stryker` legs (mutation.yml) record a passing run under a
fingerprint of the checked-out code: `.github/scripts/code-fingerprint.sh` hashes every
tracked file except prose that nothing reads (`docs/**` other than
`docs/tickets/IOPERATION-COVERAGE.md`, `.claude/**`, and the root `CLAUDE.md`,
`CONTRIBUTING.md`, `README.md`). A later PR push with the same fingerprint on the same leg
skips the build, tests and Stryker and reports the earlier pass, so a push that only edits
a ticket or an ADR comes back green in about a minute. The job still runs, so the required
check names are unchanged. Gates passes are also recorded on `main` pushes, so a prose-only
PR on a green `main` skips from its first push; Stryker passes are recorded per PR only, and
the nightly sweep never reuses one. Separately, a PR's `stryker` leg for a project whose
`src/<project>/**/*.cs` the PR does not change reports a pass without building, because the
incremental run would have no mutants (see the Mutation row). `vulnerable-packages`,
`gitleaks`, CodeQL and Sonar always run. If a test starts reading a file under an excluded
path, add it to the script's keep list in the same PR.

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
