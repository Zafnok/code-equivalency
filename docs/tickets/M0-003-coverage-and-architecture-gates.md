# M0-003 Coverage 100% gate and architecture tests
Status: done (PR #3)
Effort: M
Depends on: M0-002

## Goal
A red build whenever any `src/` assembly is below 100% line or branch coverage, or any
project references something ARCHITECTURE.md forbids.

## Deliverables
- [x] `tools/check-coverage`: a tiny .NET console project (so it is itself tested, and no
      PowerShell XML parsing). Reads every cobertura file under `TestResults/`, groups by
      `src/` assembly, fails with a table if line or branch rate < 100% for any, and fails
      if any `ExcludeFromCodeCoverage` attribute in `src/` lacks an `M?-???` ticket id in
      its Justification.
- [x] Prove the gate bites: add a temporarily uncovered method, see red, remove it
      (describe in the PR).
- [x] `tests/Equiv.Tests.Architecture` with ArchUnitNET rules: `Equiv.Core` depends on no
      `Equiv.*` and no `Microsoft.CodeAnalysis.*` or `Microsoft.Z3`; `Frontend.*` never
      references `Verify.*` and vice versa; only `Equiv.Cli` references both; IR types live
      in namespace `Equiv.Core.Ir` and are prefixed `Ir`.
- [x] `build.ps1` runs both.

## Out of scope
Stryker (M0-004).

## Notes
- `TngTech.ArchUnitNET.xUnitV3` was already pinned in `Directory.Packages.props` (M0-002)
  but not referenced by any project yet; added the `PackageReference` to
  `Equiv.Tests.Architecture` and regenerated its lock file (`dotnet restore --force-evaluate`).
- coverlet.MTP's cobertura filenames are `<Project>.coverage.cobertura.<timestamp>.xml`, not
  `<Project>.cobertura.xml` — a naive `*.cobertura.xml` glob matches nothing. `check-coverage`
  globs `*.cobertura.*.xml`; covered by `CoberturaReportDiscoveryTests`.
- Each test project's cobertura report only shows the coverage *that test project* exercised
  for a shared `src/` assembly, so a naive per-file check would false-fail. `check-coverage`
  merges line/branch hits across all reports for the same assembly before computing rates
  (`CoberturaCoverageReader`).
- `check-coverage`'s own assembly also shows up as a "package" in its test project's cobertura
  report. Without filtering, the tool would fail its own gate (its `Program.cs` entry point
  isn't unit-tested). Scoped evaluation to assemblies with a matching `src/<name>/<name>.csproj`
  via `SrcAssemblyDiscovery`; anything else (this tool, future non-src instrumented code) is
  ignored. Covered by `CoverageGateTests.AssemblyNotInSrcIsIgnoredEvenWhenUncovered`.
- Proved the gate bites by temporarily adding an uncovered method to
  `src/Equiv.Core/AssemblyMarker.cs`: first plain (drove `Equiv.Core` line/branch rate to
  0%, exit code 1), then wrapped in `[ExcludeFromCodeCoverage]` with no `Justification`
  (drove a separate "no Justification" violation, exit code 1). Reverted both before
  committing; `AssemblyMarker.cs` is back to its M0-002 state.
- ArchUnitNET's default behaviour requires a rule's `That()` predicate to match at least one
  type ("requires positive evaluation"). Since no `Ir*` types exist yet (M1-002), the two
  IR-naming rules use `.WithoutRequiringPositiveResults()` so they don't fail on an empty
  match today; they'll start enforcing as soon as `Equiv.Core.Ir` types land.
- `Equiv.Cli`'s `Program.cs` uses top-level statements, so there's no accessible marker type
  for `ArchLoader`. Loaded all four assemblies by simple name (`Assembly.Load("Equiv.Cli")`
  etc.) instead of `typeof(...).Assembly`, consistent across the loader call.
