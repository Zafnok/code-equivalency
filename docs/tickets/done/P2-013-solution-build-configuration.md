# P2-013 Projects outside the solution's build configuration are not loaded or counted
Status: done (PR #158)
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-024

## Goal
SignalR.Extras.Autofac's solution lists five projects. Only two have a `Build.0` entry in the
solution's configurations, so neither `dotnet build` nor the solution restore touches the other
three (two example sites and a `_build` script). equiv loads all five, skips the three unrestored
ones, reports a 40% project load rate and exits 4. A project the solution does not build is not
part of the product being migrated.

After this ticket, the loader opens only the projects that the solution's default configuration
(`Debug|Any CPU`, or the first listed) builds. The others are listed once in a run property
`projectsNotBuilt` and count neither as loaded nor as skipped.

## Spec references
ADR 0029 decision 1; M3-024; ADR 0028 (project load rate definition). The rate's denominator
changes, so check the `equiv-adr` bar test first: this may need a clarification line on ADR 0028.

## Acceptance criteria (all must hold; nothing beyond them)
1. An integration-test solution with one project outside the build configuration loads with
   exit 0 and lists that project in `projectsNotBuilt`.
2. `corpus.ps1 -Metrics` prints `projectsNotBuilt`.
3. VERIFICATION-MODEL's census paragraph defines the load rate over built projects.

## Size guard
Solution-configuration parsing belongs in `Equiv.Frontend.CSharp.Loading`. If MSBuildWorkspace
cannot be told which projects to open, stop and record what you found.

## Out of scope
Honouring configurations other than the default.

## Notes
- Decision: MSBuildWorkspace is told which projects to open through a solution filter. The loader
  writes a temporary `.slnf` (absolute solution and project paths) listing only the built projects,
  opens that, and deletes it; Roslyn's filter support does the rest. The parsing and filter text
  are in `Loading/SolutionBuildConfiguration`; only a `.sln` whose configuration leaves some, but
  not all, projects unbuilt gets a filter.
- Decision: a `.slnx` is always opened whole (its per-project `<Build Project="false"/>` is the
  out-of-scope "other configurations" territory, and the corpus pairs that need this are `.sln`).
  A `.sln` with no `SolutionConfigurationPlatforms` builds every project; one whose default
  configuration builds none is opened whole too, since an empty filter would be meaningless.
- Decision: `run.properties.projectsNotBuilt` is `{legacy: [names], modern: [names]}`, written on
  every run like `analysedLinesOfCode`, the names being the `.sln` project names. It is carried on
  `FrontendAnalysis` (init properties defaulting to empty), not on `MatchResult`.
- Clarification: ADR 0028's "C# projects in the solution" now reads as the projects the default
  configuration builds (dated bullet under a new `## Clarifications` in ADR 0028, per the
  `equiv-adr` bar test's first row).
- `PartialLoadTests` (M3-024) added its broken and C++ projects to the `.sln` with no
  `ProjectConfigurationPlatforms` entry, so under this ticket they were not built and the test's
  exit 4 became 0. They now get `Build.0` entries, as a real solution's would.
- Toolchain: the integration tests run on this Linux box when `TargetFrameworkRootPath` points at
  the `Microsoft.NETFramework.ReferenceAssemblies.net48` package's `build/` folder (what
  `corpus.ps1 -Env` sets up). `NotBuiltProjectTests` and `PartialLoadTests` pass there;
  `webapi-basic` (System.Web) and the `file:///` URI scrub in `ComparePipelineTests` differ on
  Linux regardless of this ticket.

