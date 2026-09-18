# M2-001 Roslyn solution loader over MSBuildWorkspace
Status: todo
Effort: L
Model: Opus, medium effort (this is toolchain debugging, not algorithm design). Sonnet at high effort is acceptable. If you are Sonnet at medium or lower, stop before doing anything else and tell the user to switch.
Depends on: M1-005, M1-001

## Goal
`Equiv.Frontend.CSharp` can load both sides of every sample: the legacy side (old-style
csproj, net48) and the modern side (SDK-style, net10.0), on Windows, and turn any
partial load into a hard failure with the diagnostics attached. `build.ps1 -Integration`
starts exercising real solutions.

## Spec references
ADR 0004 (why MSBuildWorkspace, why Windows); ARCHITECTURE.md (Cli exit code 4 on load
failure); QUALITY-GATES.md (integration tests run on windows-latest only).

## Design

Contract stays inside the frontend. `Equiv.Core` cannot see Roslyn, so `ISolutionLoader`
is `internal` to `Equiv.Frontend.CSharp`:

```
internal interface ISolutionLoader
{
    Task<LoadedSolution> LoadAsync(string solutionPath, CancellationToken ct);
}
internal sealed record LoadedSolution(Solution Solution, ImmutableArray<Compilation> Compilations);
internal sealed class SolutionLoadException(string path, ImmutableArray<LoadDiagnostic> diagnostics) : Exception;
```

Implementation `MsBuildSolutionLoader`:

1. `MSBuildWorkspace.Create(properties)` with `Configuration=Debug`, `Platform=AnyCPU`.
   Do not reference `Microsoft.Build.Locator`; Roslyn 4.9+ runs MSBuild in a separate
   build-host process and picks the .NET Framework host for non-SDK projects (needs VS
   Build Tools; on this box under `Program Files (x86)\Microsoft Visual Studio\18\BuildTools`).
2. Subscribe to `workspace.WorkspaceFailed` and collect every event. After
   `OpenSolutionAsync`, any `WorkspaceDiagnosticKind.Failure` aborts with
   `SolutionLoadException`. Warnings are kept and surfaced on `LoadedSolution`.
3. For each project, `GetCompilationAsync`, then classify compiler diagnostics: error ids
   that mean "references did not resolve" (`CS0006`, `CS0012`, `CS0234`, `CS0246`,
   `CS0400`, `CS0518`, `CS1705`, `CS8032`) abort the load; other errors are recorded
   and reported but do not abort (the user's code may genuinely not compile on one side;
   the frontend must still work on the methods that bind). Put the classification in a
   pure `CompilationDiagnosticClassifier` so it is unit-testable.
4. Reject solutions with zero C# projects and solutions containing non-C# projects
   (VB, F#): `SolutionLoadException` with a clear reason. The router (M1-005) already
   rejects by extension; this is the second line.
5. One `MSBuildWorkspace` per side, disposed after the compilations are materialised.
   Compilations are immutable and outlive the workspace.

Coverage: only the two lines that call `MSBuildWorkspace.Create` and `OpenSolutionAsync`
are excluded, in a tiny `MsBuildWorkspaceFactory` marked
`[ExcludeFromCodeCoverage(Justification = "M2-001: spawns the MSBuild build host; covered by Equiv.Tests.Integration")]`.
Everything else (event collection, classification, rejection rules, disposal) is
unit-tested against `AdhocWorkspace` through the same code path.

## Deliverables
- [ ] Types above; `MsBuildSolutionLoader`; `MsBuildWorkspaceFactory` (excluded); `CompilationDiagnosticClassifier`.
- [ ] Unit tests: classifier table (one test per id family); failure event aborts; warning
      event is kept; VB project rejected; empty solution rejected; disposal after load.
- [ ] Integration tests (`Equiv.Tests.Integration`, `[Trait("Category","Integration")]`):
      every sample's legacy and modern side loads with zero failures; a deliberately broken
      copy of `identical/legacy` with a missing reference fails with the expected classification.
- [ ] `build.ps1 -Integration` runs them; `ci.yml` already passes the flag on Windows.
- [ ] README prerequisites updated with the exact Build Tools path and component ids
      recorded in M0-001 Notes.

## Pitfalls
- `Microsoft.CodeAnalysis.Workspaces.MSBuild` ships the build hosts as content under
  `BuildHost-net472` and `BuildHost-netcore`; they must end up next to the test and CLI
  binaries. If the load fails with "build host not found", that is the cause.
- The legacy build host needs the 4.8 targeting pack installed, not just the SDK.
  Missing pack shows up as `CS0518` (predefined type not defined), not as a workspace failure.
- `WorkspaceFailed` fires on a background thread; collect into a concurrent collection.
- Web application projects import `Microsoft.WebApplication.targets` from VS; Build
  Tools does not ship it. Sample `webapi-basic` (M2-005) must avoid that import (a plain
  class library with the attributes is enough for our purposes).
- Do not time-box this against the toolchain for more than 15 minutes per problem;
  record what you saw in Notes and stop.

## Out of scope
Symbol enumeration (M2-002). Any Linux loader. Buildalyzer.

## Notes
