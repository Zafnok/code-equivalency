# P2-176 A project whose source generator is missing or throws loads without a word
Status: todo
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-172

## Goal
P2-172 found why 17 bodies of `powershell-19687` were `unbound`: 16 read constants that a source
generator of the repository declares. The generator is a project reference with
`OutputItemType="Analyzer"`. Its assembly was not on disk, so the workspace had nothing to run; once
it was built, it threw, because the MSBuild properties it reads are empty when a workspace loads
the project. In both states the project loaded, the run exited 0, and neither the console, the log
nor the SARIF said a generator had not run. The only trace was `unbound` results on bodies in files
nobody edited, and it took a ticket to find out why.

When done, a run says which generators of a loaded project did not run and why, so the next
checkout that is missing one is a line to read and not an investigation.

## Spec references
`docs/tickets/done/P2-172-*.md` (the Notes: the two states and what each one looked like);
ADR 0029 (project containment and what a load notification is); ADR 0038 (the run log);
`src/Equiv.Frontend.CSharp/Loading/MsBuildSolutionLoader.cs` (`CompileAsync`);
`.claude/skills/equiv-package-api` (Roslyn's `Project.AnalyzerReferences`,
`GetSourceGeneratorDiagnosticsAsync`, CS8785).

## Acceptance criteria (all must hold; nothing beyond them)
1. A loaded project with an analyzer reference whose file does not exist reports it: one load
   diagnostic that names the project and the file. It skips nothing.
2. A loaded project whose generator throws reports it: one load diagnostic with the generator's
   name and the compiler's own id for it (CS8785). It skips nothing.
3. Both are kept diagnostics, as a workspace warning is: no project is skipped, the exit code does
   not change, and they appear where a kept load diagnostic appears today (the SARIF's tool
   notifications and the run log). Decide the kind with `equiv-decide` and log it.
4. On `powershell-19687` fetched with only the first three steps of `tools/corpus/README.md`, a
   census run (`--lower-only`) names `SMA.Generator`'s assembly as missing. Notes record the line.

## Tests
- `MsBuildSolutionLoaderTests`: a fixture project with an analyzer reference to a file that is not
  there; one whose generator throws.

## Out of scope
Building a generator for the user. Running generators in the bare loader. Analyzer diagnostics,
which the loader does not run.

## Notes
- Filed 2026-10-13 from P2-172. No open ticket owned it.
