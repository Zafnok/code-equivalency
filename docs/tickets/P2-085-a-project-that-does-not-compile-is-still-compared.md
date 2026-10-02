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
5. `full` runs of `eshop-upgrade-assistant` and `eshop-porting-assistant` compare the web project.
   Matched pairs, and the counts by rule, go in `## Notes` next to P2-065's (2 matched pairs each).

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
- Decision: "naming its first error" -> the Unknown's detail names the first error of each erroneous side, by side and position, not every error. The diagnostic id and message are not carried: the IR holds a span per cause, and a new field on the IR or on `ProcedurePair` for a message is more than this ticket needs. Alternatives: a `ProcedurePair` property with the diagnostic text. Rule: 4.
- The ticket names `CSharpFrontend.cs` as where a project is skipped. The skip is in the loaders: `CompilationDiagnosticClassifier` classes the four name errors as unresolved references, and `MsBuildSolutionLoader`, `BareProject` and `CompositeSolutionLoader` skip on that kind. `CSharpFrontend` only passes the side.
