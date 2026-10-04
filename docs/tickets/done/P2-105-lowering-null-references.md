# P2-105 Five procedures make the lowerer throw a null reference
Status: done (PR #379)
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
- One cause, not five. All five stacks are `IrLowerer.Branch` -> `Operation` -> `CaptureRead` -> `TypeMapper.Map`, with a
  null type. It is not P2-083's fix missing a case: that one was a `Binary` with a typeless `null` literal.
- The construct: an interpolated string passed to a parameter whose handler type's constructor ends in `out bool`, the
  flag by which the handler declines the string. `System.Diagnostics.Debug.Assert(bool, ref AssertInterpolatedStringHandler)`
  is in four of the five procedures and `Debug.WriteLineIf(bool, ref WriteIfInterpolatedStringHandler)` in the fifth
  (`RevisionGraph::LoadingCompleted()`). Operation kinds: the control flow graph passes a `FlowCaptureReference` with
  `IsInitialization` true as the constructor's `out` argument, then branches on a second `FlowCaptureReference` to the
  same capture to skip the appends. That second reference has a null `Type`, and no `FlowCapture` ever stores the capture.
- Decision: lowered, not opaque. The constructor's `ObjectCreation` was already opaque with reason `ref-argument`, because
  the capture was not a writable target; making the capture the call's `refout` turns it into the call it is, and the
  branch reads what the call wrote. A typeless read of a capture is read as `bool`, which is what the language requires
  of that parameter. Had the constructor stayed opaque for another reason, the read would be the existing `undefined`
  opaque, not an exception.
- Not changed: the `Debug.Assert` and `Debug.WriteLineIf` calls themselves are still opaque with reason `ref-argument`,
  since they take the handler by `ref` and it lives in a capture.
- Criterion 1: `IrLowererTests.AnInterpolatedStringHandlerThatCanDeclineLowersOrIsOpaque` (`Debug.Assert` and
  `Debug.WriteLineIf`). On `main` both cases throw the `NullReferenceException` with the stack above.
- Criterion 2: `--lower-only` reruns at 38d9db4, against the P2-058 worktree's checkouts (already restored, and
  PowerShell's three load steps already done). `powershell-19687` exit 0 in 220s, `gitextensions-11372` exit 0 in 117s,
  `gitextensions-11284` exit 0 in 115s; each has 0 notifications and no `run.properties.unverified`. Deviation: the
  reruns are `--lower-only`, not `full`, as P2-083's were: the crash is in lowering, and a full run of
  `gitextensions-11372` takes 31 minutes for no further evidence.
