# P2-012 NuGet warnings NU1701, NU1702 and NU1903 are not project load failures
Status: todo
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-024

## Goal
MSBuildWorkspace reports some NuGet restore warnings as `WorkspaceDiagnosticKind.Failure`.
`CompilationDiagnosticClassifier` has a table of MSBuild warning codes it treats as warnings, and
these are not in it, so M3-024 skips the project. In M3-022's census:
- NU1903 (package with a known vulnerability) skipped every modern project of ServiceAnt;
- NU1701 (package restored for .NET Framework on a `net10.0` project) made SignalR.Extras.Autofac's
  whole modern side fail to load;
- the NU1702 message (a ProjectReference resolved with a different framework) skipped one legacy
  flavour of adapters-shortest-paths-dotnet. That message carries no code in the text the workspace
  passes on.

Every one of these projects builds with `dotnet build`. Agent migrations commonly leave
.NET Framework-only packages behind and old package versions in place, so this skips exactly the
code equiv is meant for. The census worked around it by restoring with
`NoWarn=NU1701;NU1702;NU1903` and `NuGetAudit=false`.

## Spec references
`CompilationDiagnosticClassifier`; `LoadDiagnosticKind`; M3-024; ADR 0029 decision 1.

## Acceptance criteria (all must hold; nothing beyond them)
1. Failure messages carrying NU1701 or NU1903, or matching NU1702's message shape, are classified as
   warnings, unit-tested with the message texts in your own words.
2. An integration test on a sample project that references a .NET Framework-only package from
   `net10.0` loads it with no skipped project.
3. `CompilationDiagnosticClassifier`'s doc comment lists the codes and why.

## Size guard
Table entries and one message pattern. No changes to M3-024's skip logic.

## Out of scope
Other NuGet warnings not seen on the corpus.

## Notes
