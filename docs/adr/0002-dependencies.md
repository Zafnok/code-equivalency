# ADR 0002: Dependency register

Status: living document. Every NuGet package in `Directory.Packages.props` has a row
here. Adding a row is part of the ticket that introduces the package. Versions were the
latest stable on nuget.org on 2026-09-17; re-checked 2026-09-18 by M0-002 (only
`System.CommandLine` changed, see its row).

| Package | Version | Used by | Why this one |
|---|---|---|---|
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.9.0 | Frontend.CSharp | Roslyn; the only complete C# semantic model |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 5.9.0 | Frontend.CSharp | out-of-process build host; loads legacy csproj via VS Build Tools MSBuild (VS2026 layout fix merged May 2026, Roslyn PR 83477) |
| Microsoft.Z3 | 4.12.2 | Verify.Z3 | official bindings, ships win/linux/osx natives. Package lags upstream (last push 2023) but the API is stable. Escape hatch: drop-in newer libz3 from GitHub releases |
| Sarif.Sdk | 5.7.0 | Core | Microsoft's SARIF 2.1.0 object model and validation |
| System.CommandLine | 2.0.12 | Cli | standard .NET CLI parser; 3.0 is prerelease (rc.1) as of 2026-09-18, so pinned to the 2.0.x stable line |
| xunit.v3 | 4.0.1 | tests | current xUnit line, native Microsoft.Testing.Platform |
| coverlet.MTP | 10.0.1 | tests | coverage under MTP; no threshold flag, hence tools/check-coverage |
| Verify.XunitV3 | 33.0.2 | tests | snapshot testing of IR dumps and SARIF |
| CsCheck | 4.9.1 | tests | property testing, C#-native, shrinking |
| TngTech.ArchUnitNET.xUnitV3 | 0.13.4 | tests | architecture rules as tests |
| Meziantou.Analyzer | 3.0.259 | all | high-signal analyzer with few false positives |
| MinVer | 8.0.0 | all | versions from git tags, zero config |
| dotnet-stryker (tool) | 5.0.0 | CI | mutation testing; MTP runner via `--test-runner mtp` |
| dotnet-sonarscanner (tool) | 11.3.0 | CI | SonarQube Cloud changegate (ADR 0009); wraps build+test, reads the opencover report `build.ps1` emits |

## Rejected
- FluentAssertions 8+ (commercial licence since Jan 2025). Use xUnit asserts; add
  AwesomeAssertions (Apache-2.0 fork) only if a ticket shows real need.
- Buildalyzer 9: good fallback if MSBuildWorkspace misbehaves on legacy csproj; not
  adopted up front because Roslyn's build host now covers the case.
- Boogie.ExecutionEngine 3.5.7: viable second backend (SymDiff approach); deferred.
- Microsoft.NET.Test.Sdk / coverlet.collector: VSTest-era, not needed under MTP.
