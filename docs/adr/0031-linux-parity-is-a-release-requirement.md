# ADR 0031: Linux parity is a release requirement; the Windows-only loader is not shippable

Status: accepted (2026-09-23); superseded in part by 0035 (runs with `--execute` are Windows-only)

## Context
ADR 0004 made the MVP loader Windows-only (legacy csproj needs VS Build Tools' MSBuild and the
.NET Framework build host) and parked a Linux "bare loader" as post-MVP. M3-004 criterion 1
therefore expects the linux-x64 binary to exit 3 on any `.sln`. But ADR 0006 names the container
as the free tier, `action.yml` with `runs.using: docker` runs only on Linux runners, and the
intended hosted tier runs on Linux containers (AKS or Azure Container Apps). As planned, the
first release ships a container and a GitHub Action that cannot analyse a single solution.
A second gap: Roslyn's MSBuildWorkspace needs an installed .NET SDK even for SDK-style projects,
so M3-004's `runtime-deps` final image could not load the modern side either.

## Decision
No release ships a surface that works on Windows and not on Linux. linux-x64 (and the container)
must load both sides of every sample and produce the same SARIF results as Windows, modulo paths,
and a CI parity job enforces this. The loading mechanism on Linux is chosen by spike ticket
M3-028 from the candidates below and recorded as a Clarification here; ADR 0004's "Windows for
the MVP" scope is superseded, its "nothing may assume the loader is MSBuild-based" rule stands.

Candidates for M3-028:
1. MSBuildWorkspace on the .NET SDK's MSBuild for both sides, with net48 reference assemblies
   from `Microsoft.NETFramework.ReferenceAssemblies.net48` (MIT) and `packages.config` packages
   restored by the tool itself.
2. ADR 0004's bare loader for the legacy side, MSBuildWorkspace (SDK) for the modern side.
3. The bare loader for both sides (no SDK in the image).

Fallback, if no candidate reaches parity on the legacy side: only loading stays on Windows. A
Windows loader worker runs the frontend and emits serialized IR (with source spans); everything
downstream (matching, Z3, SARIF) runs on Linux. The seam already exists: `ILanguageFrontend`
produces IR and ADR 0001 plans the Java frontend as a sidecar emitting IR the same way, so both
need one versioned IR wire format. Z3 itself has no Windows dependency (ADR 0030).

## Why
- Every deployment target named so far (GitHub Action, container free tier, AKS / Container Apps)
  is Linux-only in practice; Windows containers exist on AKS but not on Container Apps.
- Windows-only analysis also makes the product depend on VS Build Tools, which cannot be
  redistributed in an image (M3-004 criterion 9).

## Rejected
- Windows containers: large images, no Container Apps support, Build Tools licensing still open.
- Ship Windows-only and say so: the container and Action would be decoration.
- A composite Action on `windows-latest` as the only fix: helps GitHub users, not the container
  or hosting.

## Consequences
- M3-004 depends on M3-028 and its implementation ticket; its criterion 1 (Linux exit 3) and the
  `runtime-deps` base image in criterion 2 change once the mechanism is chosen.
- The engine-side cost is in `Equiv.Frontend.CSharp` only; Core, Verify and SARIF are already
  cross-platform, and ADR 0030 removes the Z3 Linux gap.
- ROADMAP: the first corpus run (M4-007) should include a Linux run of the same pair.

## Clarifications
- 2026-09-25 (M3-028). **Candidate 2 is chosen.** Off Windows, non-SDK (old-style) C# projects
  load through the bare loader. SDK-style projects load through MSBuildWorkspace on the .NET
  SDK's MSBuild, so the Linux image and binary need the .NET SDK. Windows keeps the current
  loader. The spike ran on `ubuntu-latest` over all 10 samples and `eshop-upgrade-assistant`.
  Candidate 2 was the only one that matched the Windows loader on every project: status, source
  files, references and errors, compared as sets.
  - Candidate 1 fails a non-SDK project with `<PackageReference>`. The SDK has no
    `ResolveNuGetPackageAssets`, which ships in Visual Studio's `Microsoft.NuGet.targets`, so every
    package reference is lost.
  - Candidate 3 fails an SDK-style web project. The Razor source generator, `RazorAssemblyInfo.cs`
    and package conflict resolution are SDK targets a bare loader would have to re-implement, one
    SDK feature at a time.
  - `packages.config` restore works without Mono or nuget.exe only when the tool does it itself.
    `dotnet msbuild -t:restore -p:RestorePackagesConfig=true` restores nothing.

  No project needed the Windows-worker fallback, so it stays unbuilt. Future residue is handled
  under ADR 0029: a non-SDK project whose MSBuild the bare loader cannot evaluate exactly (for
  example `<Choose>`, a property function in a property it reads, a target that adds `Compile` or
  `Reference` items, a COM reference) is skipped with a notification naming the construct. It is
  never loaded approximately. If the corpus load rate (ADR 0028) shows such skips on a human pair,
  that is a ticket against the bare loader first and the fallback second. Evidence is in M3-028's
  Notes. The implementation is M3-029.
