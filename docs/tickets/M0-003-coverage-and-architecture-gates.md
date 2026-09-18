# M0-003 Coverage 100% gate and architecture tests
Status: todo
Effort: M
Depends on: M0-002

## Goal
A red build whenever any `src/` assembly is below 100% line or branch coverage, or any
project references something ARCHITECTURE.md forbids.

## Deliverables
- [ ] `tools/check-coverage`: a tiny .NET console project (so it is itself tested, and no
      PowerShell XML parsing). Reads every cobertura file under `TestResults/`, groups by
      `src/` assembly, fails with a table if line or branch rate < 100% for any, and fails
      if any `ExcludeFromCodeCoverage` attribute in `src/` lacks an `M?-???` ticket id in
      its Justification.
- [ ] Prove the gate bites: add a temporarily uncovered method, see red, remove it
      (describe in the PR).
- [ ] `tests/Equiv.Tests.Architecture` with ArchUnitNET rules: `Equiv.Core` depends on no
      `Equiv.*` and no `Microsoft.CodeAnalysis.*` or `Microsoft.Z3`; `Frontend.*` never
      references `Verify.*` and vice versa; only `Equiv.Cli` references both; IR types live
      in namespace `Equiv.Core.Ir` and are prefixed `Ir`.
- [ ] `build.ps1` runs both.

## Out of scope
Stryker (M0-004).

## Notes
