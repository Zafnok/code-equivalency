# P2-095 Two procedures make the lowerer throw a null reference
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-058

## Goal
P2-058's cleanup runs exit 5 on two pairs, each with one notification that lowering "failed:
Object reference not set to an instance of an object":
- `powershell-19687`:
  `System.Management.Automation.Security.SystemPolicy::GetFilePolicyEnforcement(string,System.IO.FileStream)`
- `gitextensions-11372`: `GitUI.UserControls.RevisionGrid.Graph.RevisionGraph::LoadingCompleted()`

Each pair lands in `run.properties.unverified`. An unsupported construct must become `IrOpaque`,
never an exception. Find the construct in each, reduce it to a fixture, and lower it or make it
opaque. They may be one cause or two.

## Spec references
`.claude/skills/equiv-corpus-run/SKILL.md` (reproduce with `-Fetch powershell-19687`; the load steps
are in `docs/runs/2026-10-02-cleanup-powershell-19687/SUMMARY.md`); `docs/ARCHITECTURE.md` on
`IrOpaque`.

## Acceptance criteria (all must hold; nothing beyond them)
1. For each cause, a reduced fixture, written from scratch and not copied from the corpus,
   reproduces the null reference on `main`. After the fix it lowers to IR or to an `IrOpaque` with
   a named reason.
2. Reruns of `powershell-19687` and `gitextensions-11372` have no lowering notification and do not
   exit 5.
3. Notes name each construct.

## Tests
- `IrLowererTests.<TheConstruct>LowersOrIsOpaque`

## Out of scope
A catch-all around the lowerer. P2-082 owns a crash that ends the run.

## Notes
