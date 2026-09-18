# M0-002 Solution skeleton with gates on
Status: todo
Effort: M
Depends on: M0-001

## Goal
`./build.ps1` restores, builds warning-free with all analyzers, format-checks, and runs
one passing test per test project. Every later ticket inherits these settings.

## Deliverables
- [ ] `Equiv.slnx` (XML solution format; no `.sln`).
- [ ] `Directory.Build.props`: `TargetFramework=net10.0`, `LangVersion=latest`, `Nullable=enable`,
      `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`,
      `EnforceCodeStyleInBuild=true`, `Deterministic=true`, `ContinuousIntegrationBuild` when the
      `CI` env var is set, `RestorePackagesWithLockFile=true`, `ManagePackageVersionsCentrally=true`,
      MinVer and Meziantou.Analyzer as global package references.
- [ ] `Directory.Packages.props` with every version from ADR 0002 (re-check nuget.org).
- [ ] `.editorconfig`: dotnet/roslyn defaults + file-scoped namespaces required + `sealed` preferred.
- [ ] `src/Equiv.Core`, `src/Equiv.Frontend.CSharp`, `src/Equiv.Verify.Z3`, `src/Equiv.Cli`
      (only the Cli is an exe) with `InternalsVisibleTo` to their own test project.
- [ ] tests: the six projects listed in `tests/README.md`, xunit.v3 + MTP, one smoke test each.
- [ ] `build.ps1`: `restore --locked-mode`, `build -warnaserror`, `format --verify-no-changes`,
      `test --coverage --coverage-output-format cobertura`, then `tools/check-coverage`
      (M0-003 fills it in; a stub that passes is fine here).
- [ ] Project references only as ARCHITECTURE.md allows (M0-003 makes this a test).

## Out of scope
Any production code beyond namespace placeholders. No CI yaml (M0-004).

## Notes
