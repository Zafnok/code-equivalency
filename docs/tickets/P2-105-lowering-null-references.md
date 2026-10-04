# P2-105 Five procedures make the lowerer throw a null reference
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-058

## Goal
All three of P2-058's cleanup runs exit 5. Five pairs each give one notification that lowering
"failed: Object reference not set to an instance of an object":
- `powershell-19687`:
  `System.Management.Automation.Security.SystemPolicy::GetFilePolicyEnforcement(string,System.IO.FileStream)`
- `gitextensions-11372`: `GitUI.UserControls.RevisionGrid.Graph.RevisionGraph::LoadingCompleted()`
- `gitextensions-11284`:
  `GitCommands.CommitDataManager::TryGetCommitLog(string,string,out string,out string,bool)`,
  `GitCommands.AppSettings::GetGitExtensionsFullPath()` and
  `GitExtUtils.GitArgumentBuilder::ToString()`

Each pair lands in `run.properties.unverified`. An unsupported construct must become `IrOpaque`,
never an exception. Find the construct in each, reduce it to a fixture, and lower it or make it
opaque. They may share causes. P2-083 (done) fixed one such crash, a binary operator in a branch
condition; check first whether these are that fix missing a case.

## Spec references
`.claude/skills/equiv-corpus-run/SKILL.md` (reproduce with `-Fetch powershell-19687`; the load steps
are in `docs/runs/2026-10-02-cleanup-powershell-19687/SUMMARY.md`); `docs/ARCHITECTURE.md` on
`IrOpaque`.

## Acceptance criteria (all must hold; nothing beyond them)
1. For each cause, a reduced fixture, written from scratch and not copied from the corpus,
   reproduces the null reference on `main`. After the fix it lowers to IR or to an `IrOpaque` with
   a named reason.
2. Reruns of the three cleanup pairs have no lowering notification and do not exit 5.
3. Notes name each construct.

## Tests
- `IrLowererTests.<TheConstruct>LowersOrIsOpaque`

## Out of scope
A catch-all around the lowerer. P2-082 owns a crash that ends the run.

## Notes
