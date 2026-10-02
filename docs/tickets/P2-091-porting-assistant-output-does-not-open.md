# P2-091 Porting Assistant's output does not open: the compiler package it leaves replaces the `Csc` task
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-085

## Goal
P2-085 loads a modern project that does not compile and compares it method by method. On
`eshop-upgrade-assistant` that took the run from 2 matched pairs to 135. On
`eshop-porting-assistant` it changed nothing: the web project is still skipped, and the run still
compares 2 procedures.

The skip there was never the compile errors. MSBuildWorkspace reports three failures for the
project, and any one of them skips it (ADR 0029 decision 1):
- Restore fails with NU1605, a package downgrade the SDK treats as an error
  (`System.Diagnostics.DiagnosticSource` 5.0.1 to 5.0.0). Restoring with NU1605 demoted removes
  this one.
- "The `TargetFramework` parameter is not supported by the `Csc` task loaded from
  `microsoft.net.compilers\3.0.0`" (`ProjectName` in P2-065's run), and "The `Csc` task could not
  be initialized with its input parameters". The tool left `<PackageReference
  Include="Microsoft.Net.Compilers" Version="3.0.0" />` in the SDK-style project, and that package
  replaces the SDK's compiler task with one from 2019.

The workspace still hands back a compilation after the `Csc` failure, with the compile errors
P2-065 listed. Whether that compilation is the project's (its parse options, defines, references
and documents) or an approximation is not known. ADR 0029 never loads a project approximately, so
find out before changing anything.

## Spec references
ADR 0029 (decision 1 and the 2026-10-01 clarifications), ADR 0004,
`src/Equiv.Frontend.CSharp/Loading/CompilationDiagnosticClassifier.cs`
(`ClassifyWorkspaceFailure`), `src/Equiv.Frontend.CSharp/Loading/MsBuildSolutionLoader.cs`,
`.claude/skills/equiv-corpus-run/SKILL.md` (section 3, restore), P2-085's Notes.

## Acceptance criteria (all must hold; nothing beyond them)
1. `## Notes` records, for the Porting Assistant web project in `.corpus/`, what the compilation
   holds when the `Csc` task fails against what it holds with the `Microsoft.Net.Compilers`
   reference removed in a scratch copy: parse options, preprocessor symbols, reference count and
   document count. Counts and option names only, no source text.
2. Route the outcome through `equiv-adr`'s bar test and log a `Decision:` line. If the two
   compilations are the same, the `Csc` task failure is a workspace warning with one unit test per
   message shape, as P2-012 did for NU1701. If they differ, the project stays skipped and the
   notification says why in one sentence that names the package.
3. The corpus skill says what to do when a pair's restore fails only on a warning the SDK treats
   as an error (NU1605 here), so the next run of a tool pair does not rediscover it.
4. A `full` run of `eshop-porting-assistant` follows the decision of criterion 2. Matched pairs and
   the counts by rule go in `## Notes` next to P2-085's (2 matched pairs).

## Files
`src/Equiv.Frontend.CSharp/Loading/CompilationDiagnosticClassifier.cs` and its tests if criterion 2
says so, `.claude/skills/equiv-corpus-run/SKILL.md`.

## Tests
Named in criterion 2.

## Size guard
If criterion 2 asks for a new ADR, write it and stop.

## Out of scope
Repairing the tool's output in the corpus. Any other tool pair.

## Notes
- Found by P2-085's run of `eshop-porting-assistant`. P2-065's SUMMARY gave the compile errors as
  the reason for the skip; its `progress.log` already held the same three workspace failures.
