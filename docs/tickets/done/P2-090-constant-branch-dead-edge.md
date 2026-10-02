# P2-090 A branch on a compile-time constant lowers to a procedure the validator can read
Status: done (PR #333)
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-083 saw, while probing, that `static int M() { if (null == null) return 0; return 1; }` lowers
without throwing and that `IrValidator.Validate` then throws a bare `NullReferenceException` in
`IrChecker.CheckTargets` (`IrValidator.cs` line 86), which reads a block's terminator's successors.
So the lowerer built a block with no terminator.

The condition is a compile-time constant. Roslyn's control-flow graph keeps both edges of such a
branch and marks the block only the dead edge leads to as unreachable; the lowerer does not lower
unreachable blocks. Find how the dead edge ends up in the IR, and make the lowering produce a valid
procedure. Lowering never throws and never hands on IR the validator cannot read (CLAUDE.md,
task-loop rules).

P2-082's second finding is a `NullReferenceException` at the same kind of read
(`IrLoopAnalysis.Search`, a block's terminator's successors) on one unidentified procedure of
`openra-17989` and `duplicati-3124`. This ticket does not look for that procedure and does not run
the corpus; it says in `## Notes` whether its cause could be that one, for P2-082 to confirm.

## Spec references
`docs/ARCHITECTURE.md` (IR well-formedness), M1-002 (the validator's rules), P2-010 (the block a
branch names that was never lowered), P2-083 `## Notes` (the observation), P2-082.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test in `tests/Equiv.Frontend.CSharp.Tests` lowers the method above through `Lowered.Method`
   (which validates). Before the fix it fails with the `NullReferenceException`.
2. The root cause is written in `## Notes`, with every shape found that reaches it (operation kinds
   and types only). Each shape has a test.
3. Each shape lowers to IR that `IrValidator.Validate` accepts and that the interpreter runs to the
   value the C# returns, with no `IrOpaque`: a constant condition is fully understood, so nothing
   about it is opaque.
4. `## Notes` says whether P2-082's `IrLoopAnalysis` crash could have this cause.

## Files
`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`, `src/Equiv.Frontend.CSharp/Lowering/SwitchChains.cs`
(only if a folded chain reaches the same cause), `tests/Equiv.Frontend.CSharp.Tests/Lowering/IrLowererTests.cs`.

## Tests
Named in criteria 1 and 2.

## Size guard
If the fix needs a change to `Equiv.Core` (the validator, `SsaBuilder`'s contract with it, a new
terminator), stop: that is P2-082 criterion 3's call.

## Out of scope
Finding P2-082's procedure, and its run-level fix. Making `IrValidator` reject a block with no
terminator (P2-082 criterion 3). Folding constant conditions the compiler does not call constant.

## Notes
- Found by P2-083 (PR #332), not by any corpus run.
- Root cause: a block whose `BranchValue` has a Boolean `ConstantValue` keeps both successors in Roslyn's CFG, but
  Roslyn follows only the live one when it sets `IsReachable`. `LowerGraph` lowers reachable blocks only.
  `IrLowerer.Branch` still emitted an `IrBranch` to both, and for the unlowered one `ExceptionLowerer.Destination`
  answers `NeverReached`, a block that by design has no terminator because "no edge reaches it" (P2-010, where the
  edge really is dead: it sits behind a `finally` that never completes). Here an edge did reach it, so `SsaBuilder.Build`
  kept it and built an `IrBlock` whose `Terminator` is null. Every reader of a terminator then throws.
- Shapes (all tested): a `Literal` `true`, a `Binary` or `Unary` with a constant value, or a `FieldReference` to a
  `const bool`, as the `BranchValue` of an `if` with a `return` in one arm, of an `if`/`else`, of a `Conditional`
  expression, and of an `if` inside a `try` with a `finally`. So `if (true) return 4; return 1;` threw as well; the
  `while (true)` that `LoopsLowerWithoutOpaqueNodes` covers does not, because Roslyn emits no conditional branch for it.
  A dead edge whose block another edge reaches (`if (Debug) { ... }` with code after it, a `do`/`while (!Debug)`) was
  valid before, as a branch on a constant, and is a jump now. A dead `throw;` (`if (Debug) return 7; throw;` in a
  `catch`) was valid too but left a reachable `rethrow` opaque, which is gone. A `when (Debug)` filter is unchanged
  in what it computes.
- Second way in: `SwitchChains` folded `if (c == 1) ... if (c == 2) ...` on a `const` local into an `IrSwitch` whose
  cases named unlowered blocks. `SwitchChains.Read` now declines a test with a constant value, so each is a jump.
- Decision: the constant branch is an `IrGoto` along the live edge and its condition is not evaluated, rather than an
  `IrBranch` on a constant to a block ending in `IrUnreachable`. `IrUnreachable` is documented as never produced by the
  frontend, `while (true)` already lowers to no branch at all, and two sides that differ only in how a constant
  condition is spelled then lower to the same IR. The dead edge's rethrow block and `finally` copies are still made and
  are dropped by `SsaBuilder.Build` as unreachable, which keeps block numbering and every existing snapshot unchanged.
- Criterion 4: yes, it could, and it is the first thing P2-082 should try. `IrLowerer.Procedure` validates only in a
  `Debug.Assert`, so a Release build (the corpus runs) hands the procedure on, and `IrLoopAnalysis.Search` line 102 reads
  `frame.Block.Terminator.Successors()`, the same read that threw here. A `const bool` flag with an early `return` or an
  `else` is ordinary code. Not confirmed: no corpus run was made, and P2-082's criterion 1 still has to name the pair.
- Criteria 1 to 3: `IrLowererTests.ABranchOnAConstantLowersOnlyItsLiveEdge` (first row is the method above) and
  `IrLowererTests.ADeadEdgeThatLeavesACatchIsNotLowered`. With the fix reverted, 12 of their 13 rows fail: 9 with the
  `NullReferenceException`, the `do`/`while` on its leftover branch, the two `throw;` rows on the leftover opaque. The
  `when` filter row passes either way and is there to pin the filter's dead false exit.
- The size guard did not trip: nothing in `Equiv.Core` changed. `IrValidator` still throws on a block with no terminator
  rather than reporting it; that is P2-082 criterion 3.
