# M3-028 Spike: load both sides of every sample on Linux
Status: done (PR #185)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-024

## Goal
ADR 0031 requires Linux parity but leaves the loading mechanism open. This spike tries its three
candidates on `ubuntu-latest` against every sample and the legacy side of one corpus pair, then
records which one to build. It produces a decision and an implementation ticket, not product code.

## Spec references
ADR 0031, ADR 0004, M2-001 (loader contract, pitfalls), ADR 0028 (corpus, via `equiv-corpus-run`).

## Acceptance criteria (all must hold; nothing beyond them)
1. A throwaway branch (not merged) holds one probe per candidate. Each probe, on `ubuntu-latest`,
   loads the legacy and modern side of every `samples/` pair and of the corpus pair
   `eshop-upgrade-assistant`, and prints per project: loaded / skipped, source-file count,
   reference count, and error diagnostics count.
2. The same probe output from the current Windows loader is the reference. Notes hold a table:
   candidate × solution → match / mismatch (with the first difference).
3. Notes record, per candidate: what must be in the container image (SDK or not, extra
   packages), image size estimate, and whether `packages.config` restore works without Mono or
   nuget.exe.
4. A `## Clarifications` bullet on ADR 0031 names the chosen candidate and why. If none matches
   Windows on some projects, Notes list those projects with the MSBuild feature that blocked
   them (COM reference, custom target generating Compile items, missing import, ...) and the
   bullet says whether that residue is taken as Unknown/skipped (ADR 0029) or needs ADR 0031's
   Windows-worker fallback.
5. A new ticket `M3-029-linux-loader.md` implements the chosen candidate. Its acceptance criteria
   include a CI parity job that runs `equiv compare` on every sample on Windows and Ubuntu and
   diffs the SARIF results (ignoring paths). M3-004 gets `Depends on: M3-029`, and its criteria 1
   and 2 are rewritten so the Linux binary and the container analyse samples instead of exiting 3.

## Files
`docs/adr/0031-linux-parity-is-a-release-requirement.md` (Clarifications only),
`docs/tickets/M3-028-linux-loader-spike.md` (Notes), `docs/tickets/M3-029-linux-loader.md` (new),
`docs/tickets/M3-004-packaging.md`, `docs/ROADMAP.md`.

## Tests
None merged; the probes are evidence, not product.

## Size guard
Any change under `src/` or `tests/` on the merged branch means you built instead of measured.

## Out of scope
Implementing the loader, the container, arm64, non-C# projects.

## Notes
- Evidence: throwaway branch `spike/M3-028-probes` (not merged): one probe program
  (`spike/M3-028/Probe`) with a `--candidate reference|1|2|3` switch, and workflow
  `.github/workflows/m3-028-spike.yml`. Runs 36098996361 and 36099406484 gave identical tables.
  The reference and candidate 1 run `src/Equiv.Frontend.CSharp/Loading/*.cs` verbatim (linked
  into the probe). The bare loader is about 900 lines of probe code: a minimal MSBuild evaluator,
  a `.sln`/`.slnx` reader, a nuget.org flat-container client and a simplified PackageReference
  resolver.
- Decision: "match" compares the status, the four counts, and the sets behind them: source paths
  relative to the solution, reference file names (`project:<name>` for a project reference) and
  error ids with their locations. So equal counts with different contents are still a mismatch.
  Alternatives: counts only. Rule: 3.
- Decision: source-file count = the project's documents (MSBuild `Compile` items, including the
  files targets write to `obj/`). Source-generator trees are a separate `generated` count.
  Alternatives: `compilation.SyntaxTrees`. Rule: 1.
- Decision: the Windows reference ran on `windows-latest` with the current loader. The samples were
  restored as `build.ps1 -Integration` does. `eshop-upgrade-assistant` was fetched and restored as
  `equiv-corpus-run` does (`corpus.ps1 -Prepare`, `-Fetch`, `-Env`, then MSBuild.exe
  `-t:restore -p:RestorePackagesConfig=true` and `dotnet restore`). The Linux jobs used the same
  `corpus.ps1 -Prepare` reference assemblies through `TargetFrameworkRootPath`. Alternatives: this
  box as the reference. Rule: 3.

### Reference: Windows probe output (current loader)
status / sources / generated / references / errors, per project.

| Solution | Legacy side | Modern side |
|---|---|---|
| added-branch, added-removed, api-drift, callee-changed, identical, loop-bound-change, removed-null-check, renamed-locals | loaded 3/0/3/0 | loaded 3/0/167/0 |
| business-layer | loaded 5/0/3/0 | loaded 5/0/167/0 |
| webapi-basic | loaded 3/0/14/0 | loaded 3/0/195/0 |
| eshop-upgrade-assistant | eShopLegacy.Common loaded 8/0/12/0; eShopLegacy.Utilities loaded 3/0/11/0; eShopLegacyMVC loaded 28/0/81/0 | eShopLegacy.Utilities loaded 3/0/115/0; eShopLegacyMVC skipped (UnresolvedReference CS0234) 28/10/322/97 |

The modern eShopLegacyMVC skip is correct: the raw Upgrade Assistant output does not compile
(System.Web.Mvc and friends are gone).

### Candidate × solution (Linux, ubuntu-latest 24.04, against the Windows reference)

| Solution | 1: MSBuildWorkspace (SDK) both sides | 2: bare legacy + MSBuildWorkspace modern | 3: bare both sides, no SDK |
|---|---|---|---|
| added-branch | match | match | match |
| added-removed | match | match | match |
| api-drift | match | match | match |
| business-layer | match | match | match |
| callee-changed | match | match | match |
| identical | match | match | match |
| loop-bound-change | match | match | match |
| removed-null-check | match | match | match |
| renamed-locals | match | match | match |
| webapi-basic | **mismatch**: first difference is on the legacy side, where no C# project loads (exit 4). Windows loads it with 14 references. CS0234 `System.Web.Http` because the `Microsoft.AspNet.WebApi.Core` PackageReference never becomes a reference | match | match |
| eshop-upgrade-assistant | match | match | **mismatch**: first difference is modern eShopLegacyMVC with 27 sources vs 28 (`obj/Debug/net6.0/eShopLegacyMVC.RazorAssemblyInfo.cs` missing). It also lacks the 10 Razor source-generator trees, has 1 extra reference (`Microsoft.AI.Web.dll`) and 89 errors vs 97 |

Candidate 1 was also run with an empty `Microsoft.WebApplication.targets` stub on `VSToolsPath`.
The results were identical, so the stub is not needed. eShopLegacyMVC's unconditioned
`$(VSToolsPath)\WebApplications\Microsoft.WebApplication.targets` import (the .NET SDK has no
such file) does not fail the load. MSBuildWorkspace evidently tolerates missing imports on both
OSes, which is why this project also loads on a Build Tools box.

### Blockers found (the MSBuild feature behind each mismatch)
- Candidate 1, webapi-basic legacy: a non-SDK csproj with `<PackageReference>`
  (`RestoreProjectStyle=PackageReference`). `dotnet restore` resolves the package, but turning
  `project.assets.json` into `<Reference>` items is `ResolveNuGetPackageAssets` in
  `Microsoft.NuGet.targets`, which ships with Visual Studio/Build Tools and not with the .NET SDK
  (see also the comment in `build.ps1`). Any non-SDK project that uses PackageReference loses
  every package on Linux under candidate 1. On Linux, Roslyn's build-host manager also warns on
  every non-SDK project: "An installation of Mono MSBuild could not be found; ... will be loaded
  with the .NET Core SDK and may encounter errors" (no Mono on ubuntu-24.04, so it falls back to
  the netcore host).
- Candidate 3, eShopLegacyMVC modern (`Microsoft.NET.Sdk.Web`): the Razor source generator and
  `RazorAssemblyInfo.cs` come from the SDK's Razor targets. Package conflict resolution
  (`ResolvePackageFileConflicts`) and exact NuGet graph semantics are SDK/NuGet behaviour the
  probe approximated, and got one reference wrong. Without an SDK, every SDK target that adds
  Compile items, analyzers or references has to be re-implemented, and Sdk.Web/Razor, WPF/WinForms
  XAML and gRPC codegen are open-ended.
- No mismatch comes from COM references or custom targets, because none of the 11 solutions has
  one.

### Per candidate: image contents, size, packages.config
Measured with `docker image ls` on the runner (compressed layers expanded; default Debian tags):
`sdk:10.0` 917 MB, `aspnet:10.0` 230 MB, `runtime:10.0` 203 MB, `runtime-deps:10.0` 120 MB. A
self-contained linux-x64 probe (Roslyn, the MSBuildWorkspace package and NuGet.Packaging) is
108 MB. The net4x reference assemblies are about 111-113 MB per framework version (1.4 GB for
all 14 that `corpus.ps1 -Prepare` fetches). The candidate 3 run's package cache for the whole
set, including the net6.0/net10.0/ASP.NET targeting packs, was 736 MB.

- **Candidate 1:** needs the .NET SDK in the image (MSBuildWorkspace's netcore build host), plus
  net4x reference assemblies (`Microsoft.NETFramework.ReferenceAssemblies.*`, MIT) through
  `TargetFrameworkRootPath`. Size: SDK image, about 0.9 GB, plus reference assemblies. It needs no
  Mono (absent on ubuntu-24.04 and in the SDK image; Roslyn falls back to the netcore host).
  `packages.config` restore does not work with the SDK alone:
  `dotnet msbuild -t:restore -p:RestorePackagesConfig=true` exits 0 with "Nothing to do. None of
  the projects specified contain packages to restore." and creates no `packages/` folder. The tool
  has to restore packages.config itself (the probe's restorer extracted all 57 eShop packages
  from nuget.org into `packages/<Id>.<Version>/`). Legacy PackageReference is broken, as above.
- **Candidate 2:** the same SDK image, needed for the modern side only, plus reference assemblies.
  No Mono, no nuget.exe. The tool restores `packages.config` itself (same 57 packages). Legacy
  PackageReference assets come from the tool's own package resolution, not from MSBuild targets.
  The probe's simplified resolver was enough for webapi-basic. Matches Windows on all 11
  solutions.
- **Candidate 3:** `runtime-deps:10.0` (120 MB) plus the self-contained app (about 108 MB). It ran
  in that container with no `dotnet` and no Mono on the PATH. Reference assemblies, targeting
  packs and packages come from nuget.org at run time (cached). packages.config restore works
  without Mono or nuget.exe (the tool's own). It is the smallest image, but it fails parity on the
  one SDK-style web project in the set.
- Timing (per side): no Linux candidate was slower than the Windows reference (2-7 s).
  MSBuildWorkspace sides took 1-5 s on Linux, and bare sides about 1 s.

### Choice
Candidate 2 (ADR 0031 Clarification 2026-09-25). It is the only candidate that matched Windows on
every project, and it is the one whose failure modes are enumerable. The bare loader only has to
understand old-style csproj, a closed format that Visual Studio stopped extending. SDK-style
projects keep the real SDK, whose targets (Razor, source generators, conflict resolution,
FrameworkReference) no hand-written loader can track. Candidate 1 is broken for legacy
PackageReference. Fixing it would mean shipping VS's `Microsoft.NuGet.targets`, which is not
redistributable, or the archived `Microsoft.NuGet.Build.Tasks`. The image cost of candidate 2 over
candidate 3 is the SDK, about 0.8 GB, which is the price of parity on SDK-style projects.
- Decision: Windows keeps `MsBuildSolutionLoader` unchanged and only non-Windows uses the bare
  legacy loader. The parity job and a Windows integration test that runs both legacy loaders on
  every sample keep them in step. Alternatives: the bare legacy loader on every OS (parity by
  construction, but it drops the one loader known to be right for legacy). Rule: 4.

### Surprises
- The Windows reference itself reports NETSDK1086 ("A FrameworkReference for
  'Microsoft.AspNetCore.App' was included...", an SDK *warning*) as a `WorkspaceDiagnosticKind.
  Failure` on modern eShopLegacyMVC. It is the same class of bug P2-012 fixed for NU1701/NU1702/
  NU1903. Here it did not change the outcome, because the project is skipped for CS0234 anyway. On
  a project that compiles, it would skip the project. Not fixed here (Size guard).
- `corpus.ps1 -Prepare` and `-Fetch` work unchanged under pwsh on Linux.
- NuGet.Packaging 6.14.0, used by the probe, raises NU1901 (low-severity advisory). The
  implementation must pick a version that passes the `vulnerable-packages` job.
