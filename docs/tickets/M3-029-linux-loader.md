# M3-029 Linux loader: bare loader for non-SDK projects off Windows, and the Windows/Ubuntu parity job
Status: in-progress
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-028

## Goal
ADR 0031 as clarified by M3-028 (candidate 2). Off Windows, `Equiv.Frontend.CSharp` loads non-SDK
(old-style) C# projects with a bare loader. That loader reads project XML, takes Compile items,
resolves references from HintPaths, net4x reference-assembly packages and NuGet packages, and
builds the `CSharpCompilation` itself. SDK-style projects still load through MSBuildWorkspace on
the .NET SDK. Windows keeps `MsBuildSolutionLoader` exactly as it is. A CI parity job runs
`equiv compare` on every sample on `windows-latest` and `ubuntu-latest` and fails on any
difference in the SARIF results. After this ticket, M3-004's Linux binary and container analyse
solutions instead of exiting 3.

## Spec references
ADR 0031 (Clarification 2026-09-25), ADR 0004 (the bare loader as first described), ADR 0029
(skip, never approximate), ADR 0002 (dependencies), ADR 0028 (project load rate), M2-001 (loader
contract, Design), M3-024 (skipped projects), P2-013 (`SolutionBuildConfiguration`), M3-028
Notes (the evidence, the reference numbers and the pitfalls below). The probe on the unmerged
branch `spike/M3-028-probes` (`spike/M3-028/Probe/Bare/*.cs`) is a working starting point. It is
not production code.

## Design
- **Routing.** On Windows, `CSharpFrontend` keeps using `MsBuildSolutionLoader`. Elsewhere it uses a
  new composite `ISolutionLoader`. The composite takes the projects the solution builds
  (`SolutionBuildConfiguration`) and splits them with the same SDK-style test Roslyn's build-host
  manager uses: a root `Sdk` attribute, an `<Import Sdk=...>`, an `<Sdk>` element, or a
  `TargetFramework(s)` property. SDK-style projects go to `MsBuildSolutionLoader` through a
  `.slnf` that leaves the non-SDK ones out (P2-013's mechanism). Non-SDK C# projects go to the
  bare loader. The result is one `LoadedSolution`. Downstream code reads only `Compilations` and
  `Skipped`, so nothing past the loader changes.
- **Cross-style references.** SDK-style projects load first. A non-SDK project that references one
  gets its compilation as a `CompilationReference`. An SDK-style project that references a non-SDK
  project must bind against the bare compilation, not the copy MSBuildWorkspace evaluates through
  its netcore build host (swap it with `Compilation.ReplaceReference`).
- **Bare evaluation (non-SDK only).** Properties are read in import order, with global
  `Configuration=Debug` and `Platform=AnyCPU` that the project cannot override. Directory.Build.props
  is imported the way Microsoft.Common.props does it. Relative imports that exist are followed.
  MSBuild's own tool-path imports (`$(MSBuildToolsPath)`, `$(MSBuildBinPath)`,
  `$(MSBuildExtensionsPath*)`, `$(VSToolsPath)`) are replaced by the loader's built-in knowledge
  of Microsoft.CSharp.targets. Conditions support `==`, `!=`, `Exists`, `HasTrailingSlash`,
  `and`, `or`, `!`, parentheses and version comparisons. Items: `Compile` (wildcards, `Exclude`,
  `Remove`), `Reference`, `ProjectReference`, `PackageReference`.
- **Never approximate (ADR 0029).** A non-SDK project that uses something the bare evaluator cannot
  evaluate exactly is skipped as a C# project (error-level notification, exit 4) with a diagnostic
  naming the construct. The closed list: `<Choose>`; a property function or method call in a
  property the loader reads; a condition outside the supported grammar; a `<Target>` in the
  project or its followed imports that creates `Compile`, `Reference` or `ProjectReference` items;
  `<COMReference>`; a missing relative import that is not conditioned on `Exists`. A project that
  loads approximately could give a wrong verdict. A skipped one gives none. The same rule skips a project
  for an item reference or metadata (`@(...)`, `%(...)`) in an item or condition the loader reads, for a
  value the compiler would reject (`LangVersion`, `PlatformTarget`, `WarningLevel`,
  `TargetFrameworkVersion`), for a target framework other than .NET Framework, and for a missing input: no
  reference assemblies, a source file or `project.assets.json` that does not exist, or a reference to an
  SDK-style project MSBuildWorkspace built no compilation for (see Notes, Deviation).
- **References.** A HintPath comes first. HintPaths are written on Windows, so backslashes are
  always converted, and when the exact path does not exist it is matched case-insensitively.
  Framework assemblies come from `<root>/.NETFramework/v<x>/` in the
  `Microsoft.NETFramework.ReferenceAssemblies.<tfm>` layout, fetched per framework version on
  first use and cached. The image holds none (about 110 MB each). Implicit references, as
  measured against MSBuild: `mscorlib` always, and `System.Core` for 3.5 and later. Facades: a
  reference that depends on `System.Runtime` pulls in `Facades/*.dll`. One that depends only on
  `netstandard` pulls in `Facades/netstandard.dll` (4.7.1 and later). Before 4.7.1, the shims
  come from the SDK's `Microsoft.NET.Build.Extensions` folder, which the image has.
  A package's nuspec `<frameworkAssemblies>` are references too (webapi-basic needs
  `System.Numerics` this way).
- **Packages.** `packages.config`: the tool extracts each missing package into the folder the
  HintPaths expect (`<solution dir>/packages/<Id>.<Version>/`, or `repositoryPath` from
  nuget.config). It does not use nuget.exe, Mono or MSBuild. `dotnet msbuild
  -p:RestorePackagesConfig=true` restores nothing on Linux (M3-028). A non-SDK project's
  PackageReferences take their compile assets from NuGet's own resolution: `project.assets.json`
  from a `dotnet restore`, or NuGet's restore libraries. A hand-written graph resolver is not
  allowed. The probe's resolver was an approximation and got one reference wrong on an SDK-style
  project. Package sources come from the solution's nuget.config (default nuget.org).
- **Compilation options** come from the project, with MSBuild's defaults for .NET Framework:
  LangVersion 7.3 unless set, `DefineConstants`, `AllowUnsafeBlocks`, `CheckForOverflowUnderflow`,
  `TreatWarningsAsErrors`/`WarningsAsErrors`/`NoWarn`, `OutputType`, `Nullable`, `AssemblyName`,
  and the platform. `BoundSerialiser` reads `compilation.Options.Platform` for x87, so
  `PlatformTarget` and `Prefer32Bit` (true by default for an AnyCPU exe) must be applied exactly.
  The generated `obj/Debug/<moniker>.AssemblyAttributes.cs` (the `TargetFrameworkAttribute`) is
  part of the compilation, as it is on Windows.

## Acceptance criteria (all must hold; nothing beyond them)
1. `CSharpFrontend` uses the composite loader when `OperatingSystem.IsWindows()` is false and
   `MsBuildSolutionLoader` otherwise. The routing is unit-tested through a seam, and
   `MsBuildSolutionLoader` is unchanged.
2. On `windows-latest`, an integration test loads every sample's legacy side with both
   `MsBuildSolutionLoader` and the bare loader. It asserts, per project, the same status, source
   paths (relative to the solution), reference file names, error ids with locations, and
   `Options.Platform`.
3. Each construct in the Design's "never approximate" list skips its project with a diagnostic
   naming the construct, and other projects still load (one test per construct).
4. `packages.config` restore and non-SDK PackageReference resolution work on Linux with no
   nuget.exe, Mono or MSBuild.exe. A test restores a `packages.config` copy of a sample into a temp
   directory through a fake feed seam. nuget.config package sources are honoured.
5. Framework reference assemblies are fetched per framework version on first use into a cache
   directory that an environment variable can relocate. The Decision line names the variable.
   Nothing is fetched when the cache already holds the version.
6. A `parity` job in `.github/workflows/ci.yml` runs `equiv compare` on every sample pair on
   `windows-latest` and on `ubuntu-latest`, then diffs the two SARIF files' `runs[0].results`
   (rule id, level, message, logical locations, properties). It ignores every path, URI and
   anything else rooted in the checkout. Any difference fails the job. It is added to the
   required checks in `docs/QUALITY-GATES.md`.
7. Every new NuGet package has a line in `docs/adr/0002-dependencies.md` and passes the licence
   gate (M0-010) and the `vulnerable-packages` job. NuGet.Packaging 6.14.0 does not pass the
   latter (NU1901).
8. 100% line and branch coverage holds. The only new `ExcludeFromCodeCoverage` is the network
   factory behind the feed seam, with a justification naming M3-029.
9. README prerequisites say what Linux needs: the .NET 10 SDK (for SDK-style projects) and
   network access to the package sources, or a pre-filled cache.

## Files
- `src/Equiv.Frontend.CSharp/Loading/`: the composite loader, the bare loader and its evaluator,
  the package and reference-assembly source (behind a seam), and the SDK-style test. Keep one type
  per file, as M2-001 did.
- `src/Equiv.Frontend.CSharp/CSharpFrontend.cs` (routing only)
- `Directory.Packages.props`, `docs/adr/0002-dependencies.md`, `THIRD-PARTY-NOTICES.md` if the
  licence gate requires it
- `.github/workflows/ci.yml` (`parity` job), `docs/QUALITY-GATES.md`, `README.md`
- tests in `tests/Equiv.Frontend.CSharp.Tests` and `tests/Equiv.Tests.Integration`

## Tests
- `Equiv.Frontend.CSharp.Tests`: `NonWindowsRoutesToTheCompositeLoader`,
  `SdkStyleDetectionMatchesRoslyn` (one case per signal), `ConditionGrammar` (property test over
  generated `==`/`!=`/`and`/`or`/`!` trees against a reference evaluator),
  `HintPathsResolveWithBackslashesAndWrongCase`, `ImplicitReferencesAndFacades`,
  `NuspecFrameworkAssembliesAreReferences`, `UnsupportedConstructSkipsTheProject` (one case per
  construct), `PackagesConfigRestoresThroughTheFeedSeam`, `NuGetConfigSourcesAreHonoured`,
  `ReferenceAssembliesAreFetchedOnceAndCached`, `PlatformAndPrefer32BitAreApplied`,
  `ASdkProjectReferencingALegacyProjectBindsAgainstTheBareCompilation`.
- `Equiv.Tests.Integration` (Windows): `BareLoaderMatchesMsBuildOnEverySampleLegacySide`.
- CI: the `parity` job.

## Size guard
About 15 new source files under `Loading/`. If you are writing SDK-style evaluation (default
globs, implicit usings, targeting packs, `FrameworkReference`, source generators), stop. That is
candidate 3, which M3-028 rejected.

## Out of scope
SDK-style projects without the SDK. The Windows loader worker (ADR 0031's fallback). The
container, Dockerfile and release (M3-004). arm64. VB and F#. The NETSDK1086-as-failure
misclassification M3-028 found on Windows (its own ticket). Running `Equiv.Tests.Integration` on
Linux.

## Pitfalls (from M3-028)
- Roslyn's build host warns "An installation of Mono MSBuild could not be found" for each non-SDK
  project it opens on Linux. After the split, it should open none. If the warning shows up, a
  non-SDK project slipped through, most likely as a `ProjectReference` target of an SDK-style
  project.
- MSBuildWorkspace tolerates missing imports on both OSes. eShopLegacyMVC's unconditioned
  `WebApplication.targets` import loads without the file. The bare loader's stricter rule applies
  only to relative imports; tool-path imports are always replaced.
- A checkout's own `global.json` can pin an SDK the image lacks. The netcore build host then fails
  to start. Report that as a load failure that names `global.json`.
- The eShop corpus pair is not a CI input (ADR 0028), and `equiv-corpus-run` is Windows-only for
  now. Before closing, repeat M3-028's check by hand: fetch `eshop-upgrade-assistant` with
  `corpus.ps1` on Linux, load both sides with the new loader, compare with the Windows reference
  numbers in M3-028's Notes, and record the result in Notes.

## Notes
- Decision: the cache variable -> `EQUIV_REFERENCE_ASSEMBLIES`, default `<local application data>/equiv/reference-assemblies`
  (`~/.local/share/...` on Linux), layout `.NETFramework/v<x>/` as `tools/corpus/corpus.ps1 -Prepare` writes it, so a
  prepared corpus cache can be reused. Packages are taken at 1.0.3, the version `corpus.ps1` pins. Alternatives:
  `TargetFrameworkRootPath`, a cache under the solution. Rule: 1.
- Decision: no new NuGet package. `project.assets.json`, `nuget.config` and the v3 service index are read with
  `System.Text.Json`/`System.Xml.Linq`, and a `.nupkg` is extracted with `System.IO.Compression`; the package graph is
  never resolved by the tool (a rejected-row in ADR 0002 records why). Alternatives: NuGet.ProjectModel, NuGet.Protocol,
  NuGet.Configuration. Rule: 4.
- Decision: a non-SDK project with `PackageReference` items and no assets file is skipped ("restore it first"), as
  `ResolveNuGetPackageAssets` fails the build on Windows. Alternatives: run `dotnet restore` from the loader; load it
  without packages. Rule: 1.
- Decision: package sources come from the `nuget.config` files in the solution's directory and above (nearest wins;
  `clear`, `add`, `remove`, `disabledPackageSources`, `repositoryPath`), else nuget.org. The user-level config is not
  read. HTTP sources are v3 only; a local folder may be flat or v3. Alternatives: NuGet's full config hierarchy. Rule: 4.
- Decision: the bare evaluator evaluates as the Windows loader does: the global properties Roslyn's build host passes
  (`DesignTimeBuild`, `BuildingInsideVisualStudio`, ...), `OS=Windows_NT`, `MSBuildRuntimeType=Full`, environment
  variables as properties, and the `Solution*` properties `*Undefined*` from `Microsoft.CSharp.targets` on.
  Alternatives: the running OS's values. Rule: 1.
- Decision: a property whose value or condition the evaluator cannot evaluate is not a skip by itself. It holds a value
  naming the construct, and the project is skipped only when the loader reads it or evaluates something that depends on
  it (the ticket's "in a property the loader reads"). An import's condition and path are always read. Alternatives:
  skip on any unsupported construct anywhere. Rule: 1.
- Decision: `Microsoft.Common.props` stands for `Directory.Build.props` and the restore's `obj/<project>.*.props`;
  `Microsoft.CSharp.targets` for the `.user` file, `Directory.Build.targets` and `obj/<project>.*.targets`. Any other
  tool-path import (for example `Microsoft.WebApplication.targets`) is replaced by nothing, as M3-028 found
  MSBuildWorkspace tolerates it missing. Alternatives: import `Directory.Build.props` even without the Common.props
  import. Rule: 1.
- Decision: `obj/Debug/<moniker>.AssemblyAttributes.cs` is read from disk when a build left it, else written in memory as
  `WriteCodeFragment` would, with the `FrameworkDisplayName` from `RedistList/FrameworkList.xml`. Alternatives: always
  synthesise. Rule: 1.
- Decision: facades: the `System.Runtime` (design-time) and `netstandard` expansions are independent, as the two MSBuild
  targets are. A dependency is followed through the files beside a reference, never into the framework directory.
  Alternatives: direct references only. Rule: 1.
- Decision: SDK-style compilations are rebound to the bare compilations by assembly name, for a compilation reference and
  for a reference to the built file. A non-SDK project MSBuildWorkspace opened only as a reference target is dropped
  from its result and loaded by the bare loader. An SDK-style project that MSBuildWorkspace already skipped is not
  rebound. Alternatives: rebind by project path. Rule: 1.
- Decision: off Windows, `LoadedSolution.Solution` is MSBuildWorkspace's solution of the SDK-style projects (empty when
  there are none); the bare compilations are not added to it, since nothing downstream reads it. Alternatives: add a
  `ProjectInfo` per bare project. Rule: 4.
- Decision: the parity job compares each sample's results as a multiset of canonical JSON strings (keys sorted, the
  checkout root replaced, backslashes as slashes). Alternatives: ordered comparison. Rule: 3.
- Decision: `ProjectPath.Resolve` returns an existing path as written, so on Windows it keeps the project's casing and
  on Linux it returns the on-disk casing. The parity integration test compares paths ignoring case. Alternatives:
  always the on-disk casing (a directory walk per path on Windows too). Rule: 4.
- Deviation: the Design's "closed list" of never-approximate constructs is extended with the cases the Design now lists
  after it (item references and metadata, compiler-rejected values, a non-.NET Framework target, missing inputs). Each
  is something the bare evaluator cannot evaluate exactly or an input that is absent, so ADR 0029's rule (skip, never
  approximate) applies unchanged; the ticket text is corrected above.
