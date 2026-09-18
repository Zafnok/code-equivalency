# ADR 0004: MSBuildWorkspace on Windows for the MVP loader

Status: accepted (2026-09-17)

## Fact check on the "needs Windows API bindings" premise
The engine calls no Windows API. To read a .NET Framework 4.8 solution it needs
(a) net48 reference assemblies so symbols bind, and (b) an MSBuild that can evaluate
old-style (non-SDK) csproj. Both come from Visual Studio Build Tools on Windows.
Roslyn's MSBuildWorkspace (4.9+) spawns a .NET Framework build host for such projects,
so a .NET 10 process can load them. On Linux, (b) is unavailable for old-style projects,
so the MVP loader is Windows-only. Everything else (IR, Z3, SARIF, CLI) is
cross-platform and is tested on Ubuntu in CI.

## Decision
MVP: MSBuildWorkspace, Windows, VS 2026 Build Tools with the 4.8 targeting pack.
Post-MVP: a "bare" `ISolutionLoader` that parses csproj XML, collects Compile items,
resolves Reference/HintPath, and takes net48 references from the
`Microsoft.NETFramework.ReferenceAssemblies.net48` NuGet package, making the container
self-sufficient on Linux. Nothing in the MVP may assume the loader is MSBuild-based.

## Rejected
- Requiring users to pre-build and hand us DLLs: loses source spans for SARIF and
  compiles with two different compilers.
