# ADR 0007: Test and gate stack

Status: accepted (2026-09-17)

## Decision
xUnit v3 on Microsoft.Testing.Platform (`dotnet.config` runner setting), coverlet.MTP
for coverage with a repo tool enforcing 100% line+branch, Verify for snapshots, CsCheck
for properties, ArchUnitNET for boundaries, Stryker (MTP runner) for mutation score,
CodeQL + gitleaks + Dependabot + NuGet lock files for supply chain, MinVer for versions.
Details in `docs/QUALITY-GATES.md`.

## Why
MTP is the current test platform; VSTest is the legacy holdover. coverlet.MTP lacks a
threshold option, so a small tested console tool over cobertura XML is cheaper than
adopting coverlet.msbuild (VSTest-era). Stryker's MTP runner is new (4.13, Mar 2026),
so it starts non-blocking and is promoted when stable on this repo.

## Rejected
- TUnit: modern and fast, but less agent training data; xUnit v3 gets the same MTP benefits.
- FluentAssertions 8: licence. AwesomeAssertions only on demonstrated need.
- Nuke/Cake build DSLs: a `build.ps1` calling `dotnet` is enough and has no learning curve.
