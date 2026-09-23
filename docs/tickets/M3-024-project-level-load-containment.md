# M3-024 A project that fails to load is skipped, not the solution; unbound methods are Unknown(Unbound)
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-001, M3-001

## Goal
ADR 0029 decisions 1 and 2. Today `MsBuildSolutionLoader.ThrowIfAborting` aborts the whole run
(exit 4) on any workspace failure, any unresolved reference, or any non-C# project anywhere in the
solution. After this ticket that fault is contained to the project. The run reports everything
else, lists what it skipped, and exits 4 only after writing the SARIF. Now that code with
unresolved references is lowered, a method whose bound body is erroneous becomes
`Unknown(Unbound)` at its diagnostics, so partial loading cannot cause a false Equivalent. This
must land before M3-022, whose first corpus solution contains a C++ project.

## Spec references
ADR 0029; ADR 0023 (the `unverified` shape, exit precedence); ADR 0004; M2-001 Design steps 2 to 4;
ARCHITECTURE.md (Cli exit codes); VERIFICATION-MODEL sections 1 and 6.

## Design
The loader classifies per project instead of per solution. `LoadedSolution` gains
`ImmutableArray<SkippedProject> Skipped`, where `SkippedProject(string Name, string AssemblyName,
ImmutableArray<LoadDiagnostic> Diagnostics)`. It keeps compilations only for the projects that
loaded cleanly enough to bind. `SolutionLoadException` remains only for "zero C# projects loaded".
Core's `MatchResult` carries the skipped projects of each side as plain data (names, assembly
names, messages), so Core still does not see Roslyn. The Cli turns them into notifications and
`unverified` entries exactly as ADR 0023 does for a crashed pair. Reuse that code if M3-013 has
landed; otherwise write it here, and M3-013 reuses it.

Unbound detection lives in the frontend. A method is unbound when any diagnostic of severity
error from `SemanticModel.GetDiagnostics(body span)` falls inside its body, or when its
`IOperation` tree holds an `IInvalidOperation` or a symbol whose type is an error type. Its
procedure is a whole-body `IrOpaque` with reason `unbound`, one cause per diagnostic span.

## Acceptance criteria (all must hold; nothing beyond them)
1. A solution with one C# project that has an unresolved reference, and one that loads, reports
   results for the loadable project. The broken project appears in
   `invocations[0].toolExecutionNotifications` (level `error`, naming the project and the
   diagnostic ids), and `executionSuccessful` is false.
2. A `.vcxproj`, `.wixproj`, `.sqlproj`, `.vbproj` or `.fsproj` in the solution is skipped with a
   `warning`-level notification. It does not affect the exit code on its own.
3. `run.properties.unverified` lists the procedures of every skipped C# project that were
   enumerable. A procedure whose counterpart project, matched by assembly name, was skipped on the
   other side produces no EQ004 or EQ005.
4. With `--baseline`, a baseline result for a procedure in a skipped project is carried as
   `unchanged` with `properties.unverified: true`, never `absent`.
5. Exit codes: 4 when any C# project was skipped (after writing the SARIF). Exit 5 outranks 4 and
   both outrank 1 and 2. Exit 4 with no SARIF only when a side has zero loadable C# projects.
   `ExitCodes` documents the precedence.
6. `run.properties.loweringCensus.projectsSkipped` is `{legacy, modern}` (counts). It is added only
   if M3-014 has landed; otherwise M3-014 adds it.
7. A method whose body holds a compiler error, an `IInvalidOperation` or an error-type symbol
   lowers to a whole-body opaque with reason `unbound`. Its verdict is `Unknown(UnknownReason.Unbound)`,
   and the verdict's causes (M3-016) are the diagnostic spans when M3-016 has landed.
8. The behaviour matches ARCHITECTURE.md's exit-code rules and VERIFICATION-MODEL sections 1, 6
   and 7, which the PR that accepted ADR 0029 already updated. Correct them in this PR if the
   implementation shows they are wrong. M2-001's ticket is not edited; ADR 0029 supersedes its
   hard-failure rule.
9. A `WorkspaceDiagnosticKind.Failure` whose message carries an MSBuild *warning* code skips
   nothing. It is recorded as a warning. This was observed on 2026-09-23 when the corpus pair
   `eshop-upgrade-assistant` (net472 side) reported `MSB3270`, a processor-architecture mismatch,
   as a Failure. The classifier keeps a short table of such codes, starting with `MSB3270`, with
   one test per entry.

## Files
- `src/Equiv.Frontend.CSharp/Loading/MsBuildSolutionLoader.cs`, `LoadedSolution.cs`,
  `SkippedProject.cs` (new), `CompilationDiagnosticClassifier.cs`
- `src/Equiv.Frontend.CSharp/CSharpFrontend.cs`, `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`
  (the `unbound` whole-body reason only)
- `src/Equiv.Core/Matching/MatchResult.cs`, `src/Equiv.Core/Verdicts/UnknownReason.cs`
- `src/Equiv.Core/Reporting/SarifReportWriter.cs`, `BaselineComputer.cs`
- `src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Cli/ExitCodes.cs`
- tests in the matching projects; `tests/Equiv.Tests.Integration/PartialLoadTests.cs` (new)
- `docs/tickets/IOPERATION-COVERAGE.md` (`Invalid` row); `docs/ARCHITECTURE.md` and
  `docs/VERIFICATION-MODEL.md` only if criterion 8 finds them wrong

## Tests
- `AProjectWithAnUnresolvedReferenceIsSkippedNotTheSolution`, `ANonCSharpProjectIsSkippedWithAWarning`,
  `ZeroLoadableProjectsIsStillALoadFailure`, `SkippedProjectProceduresAreUnverifiedNotAddedOrRemoved`,
  `ABaselineResultInASkippedProjectIsCarriedUnchanged`, `ExitCodePrecedenceIsFiveFourThenVerdicts`,
  `AnUnboundMethodIsUnknownUnbound`, `AnUnboundMethodIsNeverCongruent` (the last one once M3-015 exists;
  until then M3-015 adds it), `ProcessorArchitectureMismatchIsAWarning`.
- Integration: a copy of `samples/identical` with an extra C# project missing a reference, and a
  dummy `.vcxproj` entry in the `.sln`, reports the `identical` verdicts and exits 4.

## Size guard
More than about 12 production files changed, or any change to the IR records or the encoder, means
the ticket has been misread.

## Out of scope
Loading without MSBuild (post-MVP bare loader). Retrying a failed project with other properties.
Lowering more constructs. Running the corpus (M3-022).

## Notes
Dependencies not yet landed, and what that meant here:
- M3-013 (exit 5, pair failures) is `todo`, so this ticket writes the notification and `unverified` path
  (`SarifReportWriter.Write`'s optional `notifications` and `unverified`, and `BaselineComputer`'s unverified
  carry-over) for M3-013 to reuse. There is no exit 5 yet: the precedence test is
  `ExitCodePrecedenceIsFourThenVerdicts` (4 over 1 and 2), and M3-013 extends it with 5. `ExitCodes` documents
  the full order.
- M3-014 (census) is `todo`: criterion 6 is left to M3-014, as the criterion says.
- M3-015 (congruence) is `todo`: `AnUnboundMethodIsNeverCongruent` is left to M3-015.
- M3-016 (causes) is `todo`: the unbound procedure carries one `IrOpaque` per diagnostic span, and the Unknown's
  detail lists them, so M3-016 can turn them into causes.

Decision: Core's plain-data record is `UnverifiedProject` (name, assembly name, `IsCSharp`, diagnostic strings,
unverified identities), not `SkippedProject`, which would clash with the loader's record in `CSharpFrontend`.
Decision: the loader's `SkippedProject` also carries `IsCSharp` (a non-C# skip is a warning) and the skipped C#
project's `Compilation`, when one exists, so its procedures can be listed as unverified. It is never lowered.
Decision: a workspace failure is attributed to the project whose `*.??proj` path it quotes. A path that is not
`.csproj` is a non-C# skip. A failure quoting no path, raised while a project compiles, belongs to that project.
One raised while the solution opens is a C# skip with an empty name ("a project the workspace did not name"):
an error and exit 4, but it filters no Added or Removed result.
Decision: `Unknown(Unbound)` is decided in `CompareCommand` before the backend is called, when either body holds
an `IrOpaque` whose reason is `Unknown.UnboundOpaqueReason` (`"unbound"`, a constant on `Unknown` so that the
frontend and the CLI share one spelling). Neither the IR records nor the encoder change.
Decision: the unbound check is at `IrLowerer.Lower(IMethodSymbol, ...)`, the frontend's entry point. The
`IMethodBodyOperation` overload still lowers erroneous code as it is bound, so the `Invalid`, `rethrow`,
`undefined` and `missing-return` paths keep their tests (`Lowered.ErroneousBody`).
Decision: a method is unbound on any error diagnostic in its declaration's span, not only in its body, so an
unresolved parameter type also counts. When there is none, the causes are the `IInvalidOperation`s and
error-typed operations (e.g. reading a field whose type did not resolve, whose diagnostic is on the field).

Size guard: 15 production files, not 12. Of these, `Unknown.cs` (a constant), `UnknownReason.cs` (one member),
`LoadDiagnosticKind.cs` (one member and doc comments) and `LoadedSolution.cs` (one field) are one-line changes,
and two are new records the Design asks for. No IR record or encoder changed.

README's exit-code row for 4 was updated too (it said "failed to load a solution"), because the meaning changed.

Observed with the real build host (PartialLoadTests): a `.vcxproj` entry in a `.sln` arrives as a
`WorkspaceDiagnosticKind.Failure` event quoting the project path, and the loader classifies it as a non-C# skip.
