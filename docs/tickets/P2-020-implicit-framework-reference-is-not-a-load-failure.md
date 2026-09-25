# P2-020 SDK warning NETSDK1086 (explicit implicit FrameworkReference) is not a project load failure
Status: in-progress
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-012

## Goal
MSBuildWorkspace reports the .NET SDK warning NETSDK1086 as a `WorkspaceDiagnosticKind.Failure`.
It fires when a project lists a `<FrameworkReference>` the SDK already adds implicitly, for example
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` in a `Microsoft.NET.Sdk.Web` project.
The M3-028 spike (Notes > Surprises) saw it on the modern side of the corpus pair
`eshop-upgrade-assistant` (`UpgradeAssistant-Output/eShopLegacyMVC/eShopLegacyMVC.csproj`), with the
message:

> A FrameworkReference for 'Microsoft.AspNetCore.App' was included in the project. This is
> implicitly referenced by the .NET SDK and you do not typically need to reference it from your
> project. For more information, see https://aka.ms/sdkimplicitrefs

The text carries no code. There the project was skipped for CS0234 anyway, but on a project that
compiles the failure would skip it. Upgrade tools and agents add this reference routinely, so it
lands on exactly the modern sides equiv is meant for. It is the same class of bug P2-012 fixed for
NU1701, NU1702 and NU1903.

## Spec references
`CompilationDiagnosticClassifier`; `LoadDiagnosticKind`; M3-024; P2-012; ADR 0029 decision 1.

## Acceptance criteria (all must hold; nothing beyond them)
1. A failure message matching NETSDK1086's shape (any framework name) is classified as
   `LoadDiagnosticKind.WorkspaceWarning`, unit-tested with the message text in your own words.
2. Near-miss text (a FrameworkReference named, but not the implicit-reference wording) stays a
   `WorkspaceFailure`, unit-tested.
3. `CompilationDiagnosticClassifier`'s doc comment lists NETSDK1086 and why.

## Files
- `src/Equiv.Frontend.CSharp/Loading/CompilationDiagnosticClassifier.cs`
- `tests/Equiv.Frontend.CSharp.Tests/Loading/CompilationDiagnosticClassifierTests.cs`

## Tests
- Unit: one test for the NETSDK1086 shape; one near-miss row in the existing "anything else is a
  failure" theory.

## Size guard
One message pattern and its tests. No changes to M3-024's skip logic.

## Out of scope
Other SDK warnings not seen on the corpus. An integration test: the corpus project is not in
`samples/`, and P2-012 already covers the failure-to-warning path end to end.

## Notes
Decision: matched by message shape only, not also by adding `NETSDK1086` to the code table. The
message M3-028 recorded carries no code, and the shape regex also matches if a future SDK or
logger prepends one. The pattern anchors on the two SDK sentences ("was included in the project."
and "This is implicitly referenced by the .NET SDK") with any framework name in between, so it
covers `Microsoft.WindowsDesktop.App` and the other implicit frameworks, not only ASP.NET Core.
The unit test uses `Microsoft.WindowsDesktop.App` and a made-up path so it is not a copy of the
corpus text; the near-miss row names a FrameworkReference that "could not be resolved".
