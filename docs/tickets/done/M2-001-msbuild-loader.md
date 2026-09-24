# M2-001 Roslyn solution loader over MSBuildWorkspace
Status: done (PR #24)
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

## Acceptance criteria (all must hold; nothing beyond them)
1. `build.ps1 -Integration` is green on this box and loads all five M1-001 samples,
   both sides, with zero `WorkspaceDiagnosticKind.Failure` events.
2. A copy of `samples/identical/legacy` with one `<Reference>` removed fails with
   `SolutionLoadException` whose diagnostics list the `CS0246` classification.
3. Only `MsBuildWorkspaceFactory` carries `ExcludeFromCodeCoverage`; `check-coverage`
   still reports 100% for `Equiv.Frontend.CSharp`.
4. No `Microsoft.Build.Locator` reference anywhere.

## Size guard
Four source files in `src/`. If you are writing MSBuild property logic or parsing
csproj XML yourself, stop: that is the post-MVP bare loader, not this ticket.

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
- Decision: where the loader types live -> `src/Equiv.Frontend.CSharp/Loading/`, namespace `Equiv.Frontend.CSharp.Loading`, one type per file (7 files, not 4; the extra three are `ISolutionLoader`, `LoadDiagnostic`, `LoadDiagnosticKind`, split out only because of one-type-per-file). Alternatives: project root, fewer multi-type files. Rule: 4.
- Decision: `LoadedSolution` gains a third member, `ImmutableArray<LoadDiagnostic> Diagnostics`, because the Design says to surface warnings and non-aborting compiler errors on it. Alternatives: a separate result type, a side channel. Rule: 1.
- Decision: `LoadDiagnostic(LoadDiagnosticKind Kind, string Id, string Project, string Message)`, with a closed kind enum (WorkspaceFailure, WorkspaceWarning, UnresolvedReference, CompilerError, UnsupportedSolution); the classifier maps an error id to `UnresolvedReference` or `CompilerError`. Alternatives: bool `IsFatal`, reusing Roslyn `Diagnostic`. Rule: 2.
- Decision: the unit-test seam is an internal constructor taking `Func<Workspace>` plus `Func<Workspace, string, CancellationToken, Task<Solution>>`; the public constructor passes `MsBuildWorkspaceFactory` method groups. Tests use a `Workspace` subclass because `AdhocWorkspace` is sealed in Roslyn 5.9 and cannot raise `WorkspaceFailed` or report disposal. Alternatives: a factory interface. Rule: 4.
- Decision: the non-C# rejection is unit-tested through `MsBuildSolutionLoader.UnsupportedProjects((name, language) pairs)`, which `LoadAsync` calls. A real VB project in an in-memory workspace needs `Microsoft.CodeAnalysis.VisualBasic.Workspaces`, a new package (ADR 0002). Alternatives: add that package. Rule: 4.
- Decision: `InternalsVisibleTo Equiv.Tests.Integration` on `Equiv.Frontend.CSharp`, because the ticket puts the integration tests for this internal contract in that project. This goes beyond CLAUDE.md's "matching test project only" wording, so it's flagged in the PR. Alternatives: make the loader public. Rule: 4.
- `.editorconfig`: CA1064 and CA1032 are off for `Loading/SolutionLoadException.cs` only. The ticket makes the exception internal; both rules exist for cross-assembly catch sites, and there are none (the frontend will map it to `Equiv.Core.FrontendLoadException`). Commented in the file.
- Roslyn 5.9: `Workspace.WorkspaceFailed` is superseded by `RegisterWorkspaceFailedHandler(...)` (returns an `IDisposable`), which is what the loader uses.
- BuildHost-net472/BuildHost-netcore reach the Cli and test outputs through the ProjectReference with no extra MSBuild. `BuildHost-*/Microsoft.Build.Locator.dll` is part of Roslyn's own build-host payload; this repo does not reference Locator (AC4).
- AC2 mechanics: the sample only uses mscorlib, so removing `<Reference Include="System" />` on its own changes nothing. The test's temp copy also adds one file, `using System; ... Uri Address;`. A fully qualified `System.Uri` gives CS0234 instead of CS0246 (both classify as UnresolvedReference).
- Loading the samples writes `obj/` into `samples/*/*/` (gitignored). Integration run for 10 sides plus the broken copy: ~15 s on this box.
