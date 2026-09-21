# ADR 0002: Dependency register

Status: living document. Every NuGet package in `Directory.Packages.props` has a row
here. Adding a row is part of the ticket that introduces the package. Versions were the
latest stable on nuget.org on 2026-09-17; re-checked 2026-09-18 by M0-002 (only
`System.CommandLine` changed, see its row).

Every row states the package's licence. Only the allowlist in
[ADR 0017](0017-licensing-and-ip.md) may be used for anything linked into the product; the two
standing exceptions in the table (`dotnet-sonarscanner`, `Microsoft.AspNet.WebApi.Core`) are
recorded there too.

| Package | Version | Used by | Licence | Why this one |
|---|---|---|---|---|
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.9.0 | Frontend.CSharp | MIT | Roslyn; the only complete C# semantic model |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 5.9.0 | Frontend.CSharp | MIT | out-of-process build host; loads legacy csproj via VS Build Tools MSBuild (VS2026 layout fix merged May 2026, Roslyn PR 83477) |
| Microsoft.Z3 | 4.12.2 | Verify.Z3 | MIT (confirmed 2026-09-21 from the published 4.12.2 nuspec: `<license type="expression">MIT</license>`, so `tools/licence-check` reads it as SPDX with no `policy.json` exception; upstream Z3Prover/z3 is MIT too) | official bindings, ships win/linux/osx natives. 4.12.2 is still the newest version on nuget.org as of 2026-09-21, so the package lags upstream (last push 2023) but the API is stable. Escape hatch: drop-in newer libz3 from GitHub releases |
| Sarif.Sdk | 5.7.0 | Core, Cli | MIT | Microsoft's SARIF 2.1.0 object model (`SarifLog`, `Save`/`Load`). No bundled schema/rule validator — that is `Sarif.Multitool`(`.Library`), not added; M1-004 validates by SDK round-trip instead (see its ticket Notes). Cli does not `PackageReference` it directly, but M1-005 catches `Newtonsoft.Json.JsonException` (Sarif.Sdk's own JSON dependency, transitive through Core) around `SarifLog.Load` to turn a corrupt `--baseline` file into exit 3 instead of an unhandled crash |
| System.CommandLine | 2.0.12 | Cli | MIT | standard .NET CLI parser; 3.0 is prerelease (rc.1) as of 2026-09-18, so pinned to the 2.0.x stable line |
| xunit.v3 | 4.0.1 | tests | Apache-2.0 | current xUnit line, native Microsoft.Testing.Platform |
| coverlet.MTP | 10.0.1 | tests | MIT | coverage under MTP; no threshold flag, hence tools/check-coverage |
| Verify.XunitV3 | 33.0.2 | tests | MIT (sponsorship rider above ~$10k revenue, see Directory.Build.props) | snapshot testing of IR dumps and SARIF |
| CsCheck | 4.9.1 | tests | Apache-2.0 | property testing, C#-native, shrinking |
| TngTech.ArchUnitNET.xUnitV3 | 0.13.4 | tests | Apache-2.0 | architecture rules as tests |
| Meziantou.Analyzer | 3.0.259 | all | MIT | high-signal analyzer with few false positives |
| MinVer | 8.0.0 | all | Apache-2.0 | versions from git tags, zero config |
| dotnet-stryker (tool) | 5.0.0 | CI | Apache-2.0 | mutation testing; MTP runner via `--test-runner mtp` |
| dotnet-sonarscanner (tool) | 11.3.0 | CI | LGPL-3.0 (exception: separate process, never linked or shipped) | SonarQube Cloud changegate (ADR 0009); wraps build+test, reads the opencover report `build.ps1` emits |
| nuget-license (tool) | 4.0.17 | CI, `tools/licence-check` | Apache-2.0 | reads each restored package's own nuspec (SPDX `<license>` expression or licenseUrl) so the M0-010 dependency licence gate does not trust a hand-typed row; chosen over `dotnet-project-licenses` per that ticket's Notes |
| Microsoft.AspNet.WebApi.Core | 5.3.0 | samples/webapi-basic (legacy) | MS .NET Library EULA (exception: samples only, never redistributed) | ticket M2-005; the real Web API 2 route/verb attributes for the sample's legacy side. `samples/**` is isolated from central package management (M1-001), so this is not in `Directory.Packages.props`; pinned directly in the sample's own `.csproj` instead |
| Microsoft.AspNetCore.Mvc.Core | 2.3.13 | samples/webapi-basic (modern) | Apache-2.0 | ticket M2-005; the real ASP.NET Core route/verb attributes for the sample's modern side. Last version published as a standalone package before ASP.NET Core 3.0 moved these types into the `Microsoft.AspNetCore.App` shared framework; still netstandard2.0, so a plain net10.0 class library (no web SDK, M2-001 pitfalls) can reference it for the attributes alone. Same samples-only exemption from `Directory.Packages.props` as the row above |

## Rejected
- FluentAssertions 8+ (commercial licence since Jan 2025). Use xUnit asserts; add
  AwesomeAssertions (Apache-2.0 fork) only if a ticket shows real need.
- Buildalyzer 9: good fallback if MSBuildWorkspace misbehaves on legacy csproj; not
  adopted up front because Roslyn's build host now covers the case.
- Boogie.ExecutionEngine 3.5.7: viable second backend (SymDiff approach); deferred.
- Microsoft.NET.Test.Sdk / coverlet.collector: VSTest-era, not needed under MTP.
