# P2-034 Lowering crashes with a bare `NullReferenceException` on two Git Extensions methods
Status: in-progress
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
- Root cause (both crashes, one cause): Roslyn folds an empty `throw;` block into the branch that reaches it, so
  in `catch { if (c) return ...; throw; }` (DeleteUnusedBranchesForm.RefreshObsoleteBranchesAsync) and
  `catch (X) when (...) { if (...) { ... } throw; }` (ConfigureJoinableTaskFactoryAttribute.AfterTest) the
  block's *conditional* successor is a `Rethrow` edge with no `Destination`. P2-010 only handled the rethrow as
  the fall-through; `IrLowerer.Branch` passed the conditional edge to `ExceptionLowerer.Destination`, which
  dereferenced `branch.Destination!`. Neither async nor the plugin-interface/NUnit-override shape mattered.
- Fix: `Branch` gives a rethrow on either edge its own opaque `rethrow` block, as P2-010 does for the fall-through.
- Criterion 3: both source methods fetched at `5190ba5c` and reduced to repros; both crashed before the fix at
  `ExceptionLowerer.cs:56` via `IrLowerer.Branch` and lower after it, so no separate ticket.
- Decision: repros are unit tests in `IrLowererTests` beside the P2-010 rethrow tests, not a sample pair (the ticket asks only for a standalone repro).

