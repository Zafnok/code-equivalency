# M4-001 Lower `foreach`, `using` and constructor bodies instead of making the whole body opaque
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-007, M3-010

## Goal
Under ADR 0014, a whole-body `IrOpaque` makes every input Unknown. Today the lowerer makes the whole
body opaque for any method containing a `foreach` or a `using`, and for every constructor. Those
are among the most common constructs in real C#, so the first real-world run (M4-007) would be
almost entirely Unknown. Roslyn's CFG already desugars `foreach` and `using` into calls
(`GetEnumerator`, `MoveNext`, `Current`, `Dispose`), property reads, implicit conversions and a
`try`/`finally`. Since M3-010 lowers the last two, these constructs need no special case. The
same holds for a constructor body, which is a base or `this` initializer call followed by a block.

## Spec references
VERIFICATION-MODEL.md sections 2 and 3; ADR 0014; ADR 0018 (position-keyed calls are what make
repeated `MoveNext()` calls sound); `docs/tickets/IOPERATION-COVERAGE.md`; the `equiv-extend-ir`
skill.

## Design
- **`foreach` and `using`:** delete the two whole-body checks in `IrLowerer.Lower`. Lower the CFG
  as it comes. Whatever the desugared shape contains that is still unsupported (for example the
  explicit conversion of a non-generic `Current` to the iteration type) becomes an `IrOpaque` at
  that point, not across the whole body. A struct enumerator (`List<T>.Enumerator`) is a
  value-typed local that the calls do not update in IR. That is sound here, because `MoveNext` and
  `Current` results are keyed by trace position, not by the receiver's value.
- **`lock`:** stays whole-body opaque. Its desugaring calls `Monitor.Enter(object, ref bool)`, and a
  `ref` argument to a call is still unsupported (`ref-argument`).
- **Constructors:** lower `IConstructorBodyOperation` with `ControlFlowGraph.Create`. Its
  `Initializer` is an ordinary invocation of the base or `this` constructor on the `this`
  input. Instance field and property initializers are not part of the constructor's operation
  tree. So a constructor that does not chain to `this(...)`, in a type that declares any instance
  field or auto-property initializer, stays whole-body opaque, now with reason
  `field-initializer` instead of `ConstructorBodyOperation`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A `foreach` over `IEnumerable<T>`, `List<T>` or an array lowers to calls, a loop and the
   `finally`'s `Dispose`, with zero `IrOpaque` when every element operation is covered.
   Snapshots: `ForEachOverList`, `ForEachOverIEnumerableOfInt`, `ForEachWithBreak`.
2. A `using` statement and a `using` declaration lower to a `try`/`finally` whose `finally` checks
   the resource for null and calls `IDisposable.Dispose()` through the M3-010 cast map where the
   CFG converts it. Snapshots: `UsingStatement`, `UsingDeclaration`.
3. A constructor with an explicit or implicit base initializer, in a type without instance
   field initializers, lowers with its initializer call first. A constructor chaining to
   `this(...)` lowers regardless of initializers. Otherwise it stays whole-body opaque with reason
   `field-initializer`. Tests: `ConstructorCallsItsBaseInitializerFirst`,
   `ConstructorChainingToThisIgnoresFieldInitializers`,
   `ConstructorInATypeWithFieldInitializersIsOpaque`.
4. `lock` stays whole-body opaque with reason `lock`; test unchanged.
5. `IOPERATION-COVERAGE.md` rows `ForEachLoop`, `Using`, `UsingDeclaration`,
   `ConstructorBodyOperation` are updated with their tests. Rows are added for every
   `OperationKind` the new snapshots contain that has none, because
   `EveryOperationKindInTheSamplesHasACoverageRow` enforces that.
6. `LoweringOracleGen` gains a case that sums a `List<int>` with `foreach`, and the oracle
   passes with the call oracle answering from the compiled run.

## Files
`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (or its P1-003 collaborators),
`src/Equiv.Frontend.CSharp/ProcedureEnumerator.cs` (only if constructors need the operation
fetched differently), `docs/tickets/IOPERATION-COVERAGE.md`, `tests/Equiv.Frontend.CSharp.Tests/Lowering/*`,
`tests/Equiv.TestSupport/LoweringOracleGen.cs`.

## Tests
The eight snapshots and tests named in criteria 1 to 3, and the extended oracle.

## Size guard
No `Equiv.Core` change. If `foreach` or `using` needs more than about 60 lines of special-case
lowering, stop: the point is that the CFG already desugars them.

## Out of scope
Field initializers stitched into constructors. `lock` and `ref` arguments to calls. `await
foreach`, `await using`, async methods. Primary constructors. Static constructors.

## Notes
