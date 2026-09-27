# M4-013 Frontend progress: projects loaded, procedures enumerated, pairs matched and lowered
Status: todo
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-012

## Goal
The `load` phase is where MSBuildWorkspace can spend many minutes on a large legacy solution, and
right now it shows up as a single item. After this ticket it has sub-phases with real counts:
`load-legacy` and `load-modern` (one item per project, weighted by document count),
`enumerate`, `match` and `lower` (one item per matched pair). Each is timed. At `debug`, a project
that fails to load or is skipped also produces a `Detail` line naming the project and the reason
(the same reason its ADR 0029 notification carries).

## Spec references
ADR 0038; ADR 0029 (project-level blast radius); ARCHITECTURE.md data flow.

## Acceptance criteria (all must hold; nothing beyond them)
1. `ILanguageFrontend.Analyze(string legacyPath, string modernPath, EquivConfig config, IRunLog log, CancellationToken ct)`.
   Every implementation and every test double compiles against it.
2. `CSharpFrontend` emits `Phase`/`Item`/`ItemDone`/`PhaseDone` for `load-legacy`, `load-modern`,
   `enumerate`, `match` and `lower`, in that order. The `total` of each phase equals the number of
   items it then emits.
3. At `debug`, each skipped or failed project emits one `Detail` naming the side, the project path
   and the reason.
4. With `NullRunLog`, the frontend's output (`FrontendAnalysis`) is equal to the output before this
   ticket for every sample. The existing snapshot tests pass unchanged.
5. `CompareCommand` no longer wraps `Analyze` in its own `load` phase.

## Files
- `src/Equiv.Core/ILanguageFrontend.cs`
- `src/Equiv.Frontend.CSharp/CSharpFrontend.cs`, and the loader and lowering entry points it calls
  (only to pass `log` through and emit events)
- `src/Equiv.Cli/CompareCommand.cs`
- Test doubles implementing `ILanguageFrontend`

## Tests
- `Equiv.Frontend.CSharp.Tests/FrontendProgressTests.cs` (`Emits_Phases_In_Order`, `Totals_Match_Items`, `Skipped_Project_Is_A_Debug_Detail`), using a recording `IRunLog` in `Equiv.TestSupport`
- `Equiv.TestSupport/RecordingRunLog.cs`

## Size guard
More than 10 non-test files changed: stop and re-read.

## Out of scope
Making loading faster. Parallel loading. Backend events (M4-014).

## Notes
