# M0-002 Solution skeleton with gates on
Status: in-progress
Effort: M
Depends on: M0-001

## Goal
`./build.ps1` restores, builds warning-free with all analyzers, format-checks, and runs
one passing test per test project. Every later ticket inherits these settings.

## Deliverables
- [x] `Equiv.slnx` (XML solution format; no `.sln`).
- [x] `Directory.Build.props`: `TargetFramework=net10.0`, `LangVersion=latest`, `Nullable=enable`,
      `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`,
      `EnforceCodeStyleInBuild=true`, `Deterministic=true`, `ContinuousIntegrationBuild` when the
      `CI` env var is set, `RestorePackagesWithLockFile=true`, `ManagePackageVersionsCentrally=true`,
      MinVer and Meziantou.Analyzer as global package references.
- [x] `Directory.Packages.props` with every version from ADR 0002 (re-check nuget.org).
- [x] `.editorconfig`: dotnet/roslyn defaults + file-scoped namespaces required + `sealed` preferred.
- [x] `src/Equiv.Core`, `src/Equiv.Frontend.CSharp`, `src/Equiv.Verify.Z3`, `src/Equiv.Cli`
      (only the Cli is an exe) with `InternalsVisibleTo` to their own test project.
- [x] tests: the six projects listed in `tests/README.md`, xunit.v3 + MTP, one smoke test each.
- [x] `build.ps1`: `restore --locked-mode`, `build -warnaserror`, `format --verify-no-changes`,
      `test --coverage --coverage-output-format cobertura`, then `tools/check-coverage`
      (M0-003 fills it in; a stub that passes is fine here).
- [x] Project references only as ARCHITECTURE.md allows (M0-003 makes this a test).

## Out of scope
Any production code beyond namespace placeholders. No CI yaml (M0-004).

## Notes

- Re-checked nuget.org 2026-09-18: everything in ADR 0002 was still current except
  `System.CommandLine`, whose "latest" is a 3.0 prerelease (`3.0.0-rc.1.26425.128`);
  pinned to the last stable 2.0.x release (`2.0.12`) instead. `Verify.XunitV3`'s "latest
  stable" resolved to `33.0.2`. ADR updated with both.
- `dotnet new sln --format slnx` (native in the .NET 10 SDK) generates the `.slnx` file
  directly; no manual XML needed.
- The ticket's `build.ps1` step says `test --coverage --coverage-output-format cobertura`,
  but those are VSTest/`dotnet test` built-in flags, not what coverlet.MTP exposes under
  MTP. coverlet.MTP is a testing-platform extension invoked by passing extension args
  after `--` to the test host: `dotnet test -- --coverlet --coverlet-output-format
  cobertura`. Used that instead; documenting here rather than an ADR since it's a CLI
  spelling correction, not an architecture decision.
- Each test project sets `<TestingPlatformCommandLineArguments>--coverlet-file-prefix
  $(MSBuildProjectName)</TestingPlatformCommandLineArguments>` so a solution-wide
  `dotnet test` doesn't have six test hosts race to overwrite the same
  `TestResults/coverage.cobertura.xml`.
- coverlet.MTP cannot instrument the test/controller assembly itself (architectural
  limitation of the MTP in-process model, logged as a warning, not a failure) — coverage
  numbers for `src/` projects are unaffected; only relevant once M0-003 wires the
  threshold check.
- `.editorconfig` came from `dotnet new editorconfig` (the actual dotnet/roslyn defaults
  template), with two repo-specific overrides added on top per CLAUDE.md:
  `csharp_style_namespace_declarations = file_scoped:error` and
  `dotnet_diagnostic.CA1852.severity = error` (seal internal types). That template ships
  with `insert_final_newline = false` for `[*.cs]`, so `dotnet format` strips trailing
  newlines from `.cs` files — that's expected, not a bug, given "dotnet/roslyn defaults".
- `build.ps1`'s `check-coverage` step invokes `tools/check-coverage.ps1` directly via the
  call operator (`&`) rather than shelling out to `pwsh`, since this box's default
  PowerShell is Windows PowerShell 5.1 (`powershell.exe`), not PowerShell Core.
- Each `src/` project has an `internal static class AssemblyMarker` as the "namespace
  placeholder" the ticket scopes this work down to; real types start in M1.
