# P2-085 A modern project that does not compile is still compared, method by method
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: none

## Goal
The product's main use is: a tool migrates a solution, and `equiv` says what a human still has to
check. P2-065 ran the corpus's two pairs of that kind, the raw output of .NET Upgrade Assistant and
of AWS Porting Assistant on the eShop sample. Both give nothing. The migrated web project does not
compile, `equiv` skips a project with compile errors whole (M3-024), and the run compares 2
procedures of an application that has 185 on the legacy side (exit 4, project load rate 50% on the
modern side).

Raw tool output not compiling is the normal case, not an accident of this sample: the tools port
what they can and leave the rest for a person. Upgrade Assistant's output has only binding errors
(CS0246, CS0103, CS0234: types its new references no longer provide). Porting Assistant's also has
syntax errors (CS1002, CS1513) in some files, and its restore fails (NU1605).

ADR 0029 decision 2 already says what erroneous code is: Unknown(Unbound), decided without the
solver, never evidence of equivalence. That rule is applied per pair, but a project with any compile
error never reaches it. Change the unit of containment from the project to the method: load the
project, compare every method whose body binds without error, and report each method that does not
as Unknown(Unbound) naming its first error.

## Spec references
ADR 0029 (decision 2, and the load-rate rule), M3-024 (project-level load containment, the rule this
changes), ADR 0028 (project load rate), `src/Equiv.Frontend.CSharp/CSharpFrontend.cs` (where a
project is skipped), `src/Equiv.Cli/CompareCommand.cs` (`UnboundCauses`),
`docs/runs/2026-10-01-full-eshop-upgrade-assistant/SUMMARY.md`,
`docs/runs/2026-10-01-full-eshop-porting-assistant/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Route the change through `equiv-adr`'s bar test before any code: it changes what M3-024 and
   ADR 0029 skip, and what "project load rate" counts. Log the outcome as a `Decision:` line. The
   questions it must settle: which errors still skip a whole project (a project that cannot be
   loaded at all, a missing reference that leaves most symbols unresolved), how a method in a file
   with a syntax error is treated, and whether a method that binds cleanly but calls a member of an
   erroneous type is trusted.
2. A sample pair, `samples/partly-compiling-modern`, whose modern side has one method with a binding
   error and others without. The run reports the erroneous method as EQ003 with reason `unbound`,
   verifies the others, and its SARIF snapshot is committed.
3. No verdict is ever Equivalent for a pair where either body has an error diagnostic inside its
   span. Property test or a unit test per error kind the ADR step names.
4. The exit code and the load-rate figure for such a run follow the decision of criterion 1, and
   `docs/` says so where exit 4 is described.
5. A `full` run of `eshop-upgrade-assistant` compares the web project. Matched pairs, and the counts
   by rule, go in `## Notes` next to P2-065's (2 matched pairs). The same run of
   `eshop-porting-assistant` is recorded there too. Its web project cannot be opened by MSBuild, so
   it is still skipped: that is P2-091 (see the `Deviation:` line).

## Files
`src/Equiv.Frontend.CSharp/CSharpFrontend.cs`, `src/Equiv.Cli/CompareCommand.cs`, their tests,
`samples/partly-compiling-modern/`, `tests/Equiv.Tests.Integration`, an ADR or a clarification if
criterion 1 says so.

## Tests
Named in criteria 2 and 3.

## Size guard
If criterion 1 asks for a new ADR, write it and stop; the implementation is then its own ticket.

## Out of scope
Repairing the tool's output. A legacy side that does not compile. NuGet restore failures on a
corpus pair (the skill's job).

## Notes
- Found by P2-065, at equiv ef79ff6.
- Decision: criterion 1, the vehicle -> dated clarifications on ADR 0029 (and one on ADR 0028 for the load rate), no new ADR. `equiv-adr`'s first row fits: decision 2 already says a method that carries an error diagnostic or references an error-type symbol is Unknown(Unbound), and decision 1's "has unresolved references" never spelled out a project whose references all resolve but whose source names types they do not provide. Alternatives: a new ADR (the Decision text does not change), a `Deviation:` line (the ticket does not contradict the ADR). Rule: the bar test's first row that fits.
- Decision: which errors still skip a whole project -> one that cannot be opened (workspace failure), one whose reference set is broken (CS0006, CS0518, CS1705, CS8032), one that yields no procedures, one that is not C#. On the modern side CS0012, CS0234, CS0246 and CS0400 no longer skip. No "most symbols unresolved" threshold: CS0518 and the workspace failure are that case, and a share threshold would be a number fitted to two runs. Alternatives: skip when more than half the procedures are unbound, skip nothing. Rule: 4.
- Decision: the legacy side keeps M3-024's rule (the four name errors still skip its project) -> it is out of scope here, and the clarification's argument only holds against a reference side that binds. Alternatives: the same rule on both sides (one parameter fewer, but a half-bound legacy side could make a pair Equivalent that is not). Rule: 3.
- Decision: a method in a file with a syntax error -> every method declared in that file is Unknown(Unbound) at the file's first syntax error. Observed with Roslyn: a missing `}` makes the next method a local function of the one before it, and an extra `}` turns later members into top-level statements while the earlier methods carry no diagnostic. Alternatives: only the methods whose span holds the diagnostic. Rule: 3.
- Decision: a method that binds cleanly but calls a member of an erroneous type -> trusted as bound; the callee's own pair is Unknown(Unbound) and shows under the caller's `unprovenAssumptions` (ADR 0019). Two exceptions, both observed to lower with no diagnostic and no error-typed operation: a constructor of a type whose base type did not resolve (no base constructor call in the bound body), and an accessor of a property whose type did not resolve. Both are Unknown(Unbound). Alternatives: every method of a type with an unresolved base is unbound. Rule: 3.
- Decision: exit code and load rate -> a project compared method by method is loaded: no notification, not in `projectsSkipped`, no exit 4; the exit code follows the verdicts (EQ003 for each unbound method). Alternatives: keep exit 4 for any project with a compile error. Rule: 1 (decision 2 makes them verdicts).
- Decision: how the loader knows the side -> `ISolutionLoader.LoadAsync` takes Core's `Codebase`, and `CompilationDiagnosticClassifier.Classify` takes it too. Alternatives: a constructor flag and two loaders in the frontend; re-admitting skipped projects in the frontend (a modern side with one project would already have thrown "no C# project that loads"). Rule: 2.
- Decision: "naming its first error" -> the errors are the Unknown's causes (SARIF `relatedLocations`), in source order, so the result's location is the modern side's first error. The detail only says which side does not bind. Before, the detail was `modern: unbound at <absolute path> <line>:<column>` and there were no causes; the result fingerprint hashes the detail, so an unbound result's fingerprint, and its `baselineState`, changed with the checkout directory and with any edit above the error. No sample had an unbound result, so no snapshot showed it. VERIFICATION-MODEL section 6 already said the diagnostics are the causes and that causes are not fingerprinted. The diagnostic id and message are not carried: the IR holds a span per cause, and a new field on the IR or on `ProcedurePair` for a message is more than this ticket needs. Alternatives: keep the position in the detail with the file name only. Rule: 1.
- The ticket names `CSharpFrontend.cs` as where a project is skipped. The skip is in the loaders: `CompilationDiagnosticClassifier` classes the four name errors as unresolved references, and `MsBuildSolutionLoader`, `BareProject` and `CompositeSolutionLoader` skip on that kind. `CSharpFrontend` only passes the side.
- Deviation: criterion 5 asked for both tool pairs to compare the web project. `eshop-upgrade-assistant` does. `eshop-porting-assistant` cannot under ADR 0029 decision 1, which this ticket leaves as it is for a project that cannot be opened: MSBuildWorkspace reports workspace failures for that project, and they were there in P2-065's run too, under the compile errors its SUMMARY named. Restore fails on NU1605 (a package downgrade the SDK treats as an error). With NU1605 demoted for that restore, which is the corpus skill's side and touches no source, two failures remain: the `Microsoft.Net.Compilers` 3.0.0 package the tool left in the project replaces the SDK's `Csc` task with one that rejects the `TargetFramework` parameter, and the task cannot be initialised. Criterion 5's text is corrected above, and the pair is P2-091.

Criterion 5, `full` runs at equiv 8adc124, one after the other on one checkout (`.corpus/`, fetched and restored as the skill says):

| | `eshop-upgrade-assistant` | P2-065 | `eshop-porting-assistant` | P2-065 |
|---|---|---|---|---|
| exit | 5 | 4 | 4 | 4 |
| modern C# projects loaded | 2 of 2 | 1 of 2 | 1 of 2 | 1 of 2 |
| project load rate | 100% (5 of 5) | 80% | 80% (4 of 5) | 80% |
| procedures, legacy / modern | 192 / 258 | 55 / 2 | 55 / 2 | 55 / 2 |
| matched pairs | 135 | 2 | 2 | 2 |
| congruent pairs | 101 | 0 | 0 | 0 |
| changed pairs (without opaque / whole-body opaque) | 31 (10 / 6) | 2 (2 / 0) | 2 (2 / 0) | 2 (2 / 0) |
| EQ001 | 101 | 0 | 0 | 0 |
| EQ002 | 7 | 0 | 0 | 0 |
| EQ003 | 21 | 0 | 0 | 0 |
| EQ004 | 123 | 0 | 0 | 0 |
| EQ005 | 57 | 53 | 53 | 53 |
| EQ006 | 3 | 2 | 2 | 2 |
| unverified procedures | 3 | 308 | 299 | 299 |

- `eshop-upgrade-assistant`: the web project loads and is compared. All 101 Equivalent results are by congruence. The 21 Unknown are 17 `unbound`, 2 `opaque`, 1 `timeout` and 1 `unaligned-loop`; by scope, 19 `method` and 2 `line`. Every `unbound` result says "the modern body does not bind" and points at its first error. No project is skipped and there is no skipped-project notification.
- Its exit code is 5, not 0: three pairs fail in lowering, `CatalogController::Details(int?)`, `::Edit(int?)` and `::Delete(int?)`. They are the three P2-083 names for `eshop-manual`, on the same legacy code, reached here for the first time because the project is now compared. They are the 3 unverified procedures.
- `eshop-porting-assistant`: unchanged from P2-065, for the reason in the `Deviation:` line. The row is the run with NU1605 demoted for the restore; restored as the skill says, the run is the same except that the notification also holds the NU1605 failure.
- 112 of the 123 Added results are `AspNetCoreGeneratedDocument.*`, the Razor views the modern project's source generator compiles. They have no legacy counterpart because the legacy views are not compiled into the project.
- Four of the 7 Divergent results are controller constructors. Both sides bind: the legacy one calls `System.Web.Mvc.Controller`'s constructor and the modern one `Microsoft.AspNetCore.Mvc.Controller`'s. Not adjudicated here (P2-047's scope).
- The census counts a pair as whole-body opaque only when a body is exactly one instruction (`LoweringCensus.IsWholeBodyOpaque`). An unbound body has one opaque per cause, so 11 of the 17 unbound pairs, those with more than one error, are missing from `pairsWholeBodyOpaque` (6). Not changed here.
- `samples/partly-compiling-modern`: the legacy side has to compile against the test host's own framework as well as net48, because `IlSamples` (the IL unit tests) compiles every sample's sources in memory and asserts no error. So the modern side's errors come from a file the migration did not carry over, not from a .NET Framework API. `IlSamples` and `IlLoweringParityTests` leave the side that does not compile out: it emits no IL.
- `Release: minor` on the PR's last commit (0.x): a modern project that does not compile no longer causes exit 4, and an unbound result's message, related locations and fingerprint change, so a baseline holding an unbound result reports it `new` once.
