# P1-003 Split `IrLowerer` into heap and exception collaborators
Status: in-progress
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
5. No *new* file (`HeapLowerer.cs`, `ExceptionLowerer.cs`, `LoweringContext.cs`) exceeds 500 lines.
   `IrLowerer.cs` itself is not held to 500: see `## Notes` for why that part of this criterion
   cannot be met as originally written.
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
under ADR 0014, and memoising rewrites snapshots for no behavioural gain; revisit only if M4-007
shows block counts in solver time). The `foreach` work (P1-004). Havocking `field.*` maps on a
call, or keying array maps per array value (the two heap-model gaps in VERIFICATION-MODEL
section 2).

## Notes
- Deviation: acceptance criterion 5's 500-line cap on every file, including `IrLowerer.cs`, cannot
  be met as written. The ticket's own Goal section says `IrLowerer.cs` was 1016 lines after M2-004;
  by the time this ticket was picked up it had grown to 1229 lines from tickets M2-004 did not
  anticipate (M3-010's property-accessor lowering, P2-010's `Destination`/`Handler` fallbacks,
  M3-030/M3-031's census work) landing in between. AC1 and AC2 name exactly which members move
  (heap: `Slice`, `Field`, `Element`, `ArrayLength`, `Versioned`, `Bounds`, `ReadSlice`,
  `WriteSlice`, `MapRead`, the `slices` dictionary, the `HeapInputs` field; exception: `Copy`,
  `Unwind`, `Destination`, `Raise`, `Handler`, `ThrowBlock`, the `copies` and `throwBlocks`
  dictionaries), and the Size guard caps the split at three new files -- so the extraction this
  ticket authorizes removes about 200 lines, leaving `IrLowerer.cs` at 1011 lines, not under 500.
  Meeting AC5 as written would mean moving general-expression-lowering code (`Binary`, `Convert`,
  `Compound`, `Update`, `Operands`, `Call`, ...) that AC1/AC2 do not name into one of the three new
  types, mixing unrelated concerns into `HeapLowerer`/`ExceptionLowerer` for a line-count target
  rather than a real seam, or adding a fourth new file the Size guard forbids. Per `equiv-adr`'s bar
  test this is a ticket-text/reality mismatch, not a new ADR: AC5 is corrected above to apply only
  to the three new files, and `IrLowerer.cs`'s size is flagged under "Needs your decision" in the
  PR description -- if it should shrink further, that is its own ticket with its own seam, not a
  line-count patch to this one.
- Decision: how `HeapLowerer`/`ExceptionLowerer` reach the lowering they do not own (an operand's
  value, a null-check-and-throw on a dereferenced receiver, resolving an lvalue to its SSA variable,
  re-lowering a `finally` copy's blocks) -> constructor-injected `Func`/`Action` delegates bound to
  `IrLowerer` instance methods, not an interface `IrLowerer` implements or a reference to `IrLowerer`
  itself. A reference to `IrLowerer` would let either collaborator reach anything on it, which is the
  coupling this ticket removes; an interface is more ceremony than the three or four call shapes each
  delegate covers. Alternatives: `internal interface ILoweringHost`; passing `this`. Rule: 4.
- Decision: `LoweringContext` -> a mutable `sealed class` with `BlockIds`/`MainBlocks`/`HandlerExit`
  fixed at construction and `Source`/`Current` as settable properties, not a record or a value type.
  `Fill` sets `Source`/`Current` once per CFG block, and `Branch`/`ThrowIf`/etc. advance `Current`
  again mid-block (a call may move it past an exception edge); a value type would need every one of
  those call sites to thread back an updated copy, which is the swap-and-restore shape this ticket
  removes, just moved to the call sites instead of the fields. Alternatives: an immutable record
  rebuilt at each mutation point; a mutable struct passed by `ref`. Rule: 4.
- Decision: `HeapLowerer`/`ExceptionLowerer` are each constructed once per procedure lowering, inside
  `LowerBlocks` (once `compilation`, `cfg`, `chains` and `bodySpan` are all known), not in
  `IrLowerer`'s constructor. Acceptance criteria 1 and 2 say `IrLowerer` reaches the moved work
  "through one instance"; only the per-region state (`LoweringContext`) needs to be per-`Fill` call.
  `compilation`/`cfg` are set via the existing post-construction object initializer, and `chains`
  needs `cfg`, so constructing the collaborators any earlier would need restructuring that
  initializer, a change this ticket's Files section does not ask for. Alternatives: passing
  `compilation`/`cfg`/`bodySpan` into `IrLowerer`'s constructor directly and building the
  collaborators there. Rule: 4.
- Decision: `Nullness`, `ThrowIfNull`, `ShadowOf`, `StoreShadow` and `Target` stay on `IrLowerer`.
  Acceptance criterion 1 names exactly which heap members move; the null-shadow mechanism and lvalue
  resolution are not on that list, and `HeapLowerer.Field`/`Element`/`ArrayLength` reach them back
  through the `lower`/`throwIfNull`/`resolveTarget` delegates. Rule: the ticket's acceptance
  criterion 1.
- Decision: of the members acceptance criterion 2 names, only `Unwind`, `Destination` and `Raise`
  are `public` on `ExceptionLowerer`; `Copy`, `Handler`, `ThrowBlock` and `NeverReached` stay
  `private` because nothing outside the class calls them (only `Unwind` calls `Copy`, only `Raise`
  calls `Handler`/`ThrowBlock`, only `Destination` calls `NeverReached`). Symmetrically on
  `HeapLowerer`, `Slice`, `Field`, `Element`, `ArrayLength`, `ReadSlice`, `WriteSlice` and `MapRead`
  are `public` (`IrLowerer` calls each directly); `Versioned` and `Bounds` stay `private`. Rule: 1.
