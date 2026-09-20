# P1-003 Split `IrLowerer` into heap and exception collaborators
Status: todo
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
`IrLowerer.cs` is 1016 lines after M2-004, and `Copy` swaps four mutable fields and restores
them while it re-lowers a `finally`. Extract the heap work and the exception-region work into
their own types, and replace the swapped fields with a `LoweringContext` passed as a parameter.
No behaviour changes: every existing test and every `.verified.txt` snapshot stays byte-identical.

## Why it is its own ticket
M2-004's size guard is per construct, and no construct is near its 150-line limit, so nothing in
that ticket forced a split. What forces it is the second defect found reviewing PR #30: `Raise`
resolved a matching `catch` as `blockIds[handler.FirstBlockOrdinal]`, but `Copy` had swapped
`blockIds` for the copy's private map, so any `finally` able to raise, nested in the `try` of a
`try`/`catch`, threw `KeyNotFoundException` out of a lowerer documented never to throw. Nothing
in the type said which methods were safe to call while that swap was live. Commit `4df213f`
fixes the bug with a `mainBlocks` fallback in `Handler`; it leaves the shape that produced it.
A context passed explicitly makes that class of bug a compile error.

## Why it comes before P1-004
P1-004 pattern-matches the enumerator's `Try`/`Finally` region and lowers array element reads.
It adds to both paths this ticket extracts, and to the same swapped state. Run in the other
order, the refactor is over more code and the new code is written against the shape being removed.

## Spec references
ARCHITECTURE.md (Frontend component rules); QUALITY-GATES.md; M2-004 acceptance criteria 4 and 6
and its Notes; the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. The heap work moves to `src/Equiv.Frontend.CSharp/Lowering/HeapLowerer.cs`: `Slice`, `Field`,
   `Element`, `ArrayLength`, `Versioned`, `Bounds`, `ReadSlice`, `WriteSlice`, `MapRead`, the
   `slices` dictionary and the `HeapInputs` field. `IrLowerer` reaches them through one instance.
2. The exception-region work moves to `src/Equiv.Frontend.CSharp/Lowering/ExceptionLowerer.cs`:
   `Copy`, `Unwind`, `Destination`, `Raise`, `Handler`, `ThrowBlock`, the `copies` and
   `throwBlocks` dictionaries. Re-lowering a region's blocks stays in `IrLowerer`; the new type
   calls back into it through a delegate, not by naming `IrLowerer`'s type.
3. `blockIds`, `mainBlocks`, `handlerExit`, `source` and `current` are not fields of `IrLowerer`,
   `HeapLowerer` or `ExceptionLowerer`. They are members of one `LoweringContext`
   (`src/Equiv.Frontend.CSharp/Lowering/LoweringContext.cs`), created per lowered region and
   passed as a parameter to every method that reads them. No method assigns one of them on
   entry and restores it on exit.
4. Every `.verified.txt` under `tests/Equiv.Frontend.CSharp.Tests/Lowering/` is byte-identical to
   its state before this ticket, and no assertion in an existing test changes. Tests may move
   between files and be renamed after the new types.
5. No file under `src/Equiv.Frontend.CSharp/Lowering/` exceeds 500 lines.
6. Every new type is `sealed` and `internal`, and the architecture tests still pass.

## Files
- edit `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`
- new `src/Equiv.Frontend.CSharp/Lowering/HeapLowerer.cs`
- new `src/Equiv.Frontend.CSharp/Lowering/ExceptionLowerer.cs`
- new `src/Equiv.Frontend.CSharp/Lowering/LoweringContext.cs`
- edit `tests/Equiv.Frontend.CSharp.Tests/Lowering/IrLowererTests.cs` (moves and renames only)
- this ticket's `## Notes`

## Tests
No new behaviour, so no new behavioural test. These four keep their names wherever they end up;
they are the regression cover for the defects this shape produced:
`AThrowInsideAFinallyGoesToACatchOutsideIt`, `ACallThatThrowsInsideAFinallyGoesToACatchOutsideIt`,
`AThrowInsideAFinallyGoesToACatchInsideThatFinally`, `AFoldedSwitchRunsTheFinallyOnEveryEdge`.
Add a unit test only where the split leaves a new type's branch uncovered by the 100% gate.

## Size guard
More than three new `src/` files, any change to a `.verified.txt`, or any change to `SsaBuilder`,
`ExceptionRegions`, `SwitchChains` or `HeapInputs` beyond a signature edit means you have misread
the ticket.

## Out of scope
Any behaviour change. Memoising the shared `IrOpaque(null, "call-throw-in-try")` block that
`Raise` mints per raising instruction (deliberately left: the verdict is identical either way
under ADR 0014, and memoising rewrites snapshots for no behavioural gain; revisit only if M3-005
shows block counts in solver time). The `foreach` work (P1-004). Havocking `field.*` maps on a
call, or keying array maps per array value (the two heap-model gaps in VERIFICATION-MODEL
section 2).

## Notes
