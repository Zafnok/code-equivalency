# P2-034 Lowering crashes with a bare `NullReferenceException` on two Git Extensions methods
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
M4-007's first real run found two `Lowering ... failed: Object reference not set to an instance of
an object.` crashes on Git Extensions:
`GitExtensions.Plugins.DeleteUnusedBranches.DeleteUnusedBranchesForm::RefreshObsoleteBranchesAsync()`
and `CommonTestUtils.ConfigureJoinableTaskFactoryAttribute::AfterTest(NUnit.Framework.Interfaces.ITest)`.
A bare `NullReferenceException` with no message means something in the frontend dereferenced a
null it assumed present; find what shape of code triggers it (an async method calling into a
plugin-interface type for the first, an NUnit attribute override for the second — look for a
common thread, e.g. both are less-common override/interface-implementation shapes the frontend's
symbol lookup does not expect).

## Spec references
Whichever lowering step's stack trace names the exact line (capture it with a debugger or added
diagnostics — the crash message alone does not have a stack trace in the SARIF notification,
`ExceptionData.Stack` in the raw SARIF might; check there first before re-deriving one).

## Acceptance criteria (all must hold; nothing beyond them)
1. Reproduce at least one of the two crashes with a small standalone repro.
2. Fix it so lowering either succeeds or falls back to a whole-body opaque, never throws.
3. Confirm the other real occurrence also stops crashing (or file it separately if it turns out to
   be an unrelated cause — record the decision in Notes).

## Size guard
If the two crashes turn out to have unrelated root causes, split the second into its own ticket
rather than widening this one.

## Out of scope
General defensive null-checking across the frontend; this is about these two shapes only.

## Notes
