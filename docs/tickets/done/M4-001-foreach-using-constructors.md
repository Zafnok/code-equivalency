# M4-001 Lower `foreach`, `using` and constructor bodies instead of making the whole body opaque
Status: done (PR #171)
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
  Two general arms were still missing (see the `Deviation:` note): the identity `Conversion` the CFG
  wraps around the collection is its operand, and the `finally`'s `IIsNullOperation` on a
  reference-typed enumerator or resource reads its nullness as `x == null` does.
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
- Deviation: the Design said M3-010 left nothing to lower, but the desugared shape has two more
  operations that were opaque: the identity `Conversion` the CFG wraps around every `foreach`
  collection (`List<int>` to `List<int>`), and the `IIsNullOperation` the `finally` makes of a
  reference-typed enumerator or resource. Criterion 1's zero-opaque `ForEachOverIEnumerableOfInt`
  cannot hold without them. Both are general arms of a few lines in `IrLowerer.Lower`/`Convert`, not
  `foreach` special cases: an identity conversion is its operand, and a reference-typed `IsNull`
  reads `Nullness` (the shadow or `null.<Sort>`), as `x == null` does. A non-reference operand
  (`Nullable<T>`) stays opaque `IsNull` for P2-008. The Design section is corrected above.
- Decision: a static constructor -> stays whole-body opaque with its old reason
  `ConstructorBodyOperation`. Alternatives: lower it like an instance one (static field initializers
  have the same gap), give it `field-initializer`. Rule: 4 (Out of scope names static constructors).
- Decision: a primary constructor with base arguments -> stays whole-body opaque
  `ConstructorBodyOperation`; the arm requires a `ConstructorDeclarationSyntax`. Roslyn binds that
  constructor's `IConstructorBodyOperation` to the type declaration, and `GetDeclaredSymbol` on it is
  the type, so lowering it threw `InvalidCastException` (found by probing; test
  `APrimaryConstructorWithBaseArgumentsIsOneOpaque`). Alternatives: lower it. Rule: 4 (out of scope).
- Decision: `this` -> always the `this` input of the method's containing type, whatever type the
  instance reference has. A `base(...)` initializer's receiver is typed as the base, so `this` was
  created as sort `B` and the later `this.f` write (map keyed by `C`) failed validation; `base.N()`
  in an ordinary method had the same latent bug. Alternatives: pass the upcast through the cast map
  (a fresh value, so `this` and the base call's receiver would be unrelated). Rule: 1 (C#'s `base`
  is the same object).
- Decision: instance field/property initializer detection -> any non-static member whose declaring
  syntax is a `VariableDeclaratorSyntax` or `PropertyDeclarationSyntax` with an initializer (fields,
  field-like events, auto-properties); `const` members are static and do not count. Alternatives:
  walk the type's syntax trees. Rule: 4.
- Decision: the lowering oracle's list -> a `List<int> l = { A, B }` parameter and a `ForEach`
  statement `foreach (int w in l) { x op= w; Body }` with `op` from the arithmetic operators, so an
  element is also a zero divisor or shift count and the body throws out through the `finally`. The
  IR's enumerator calls are answered by a boxed real `List<int>` enumerator per `GetEnumerator`,
  which is "answering from the compiled run" without intercepting it. Alternatives: a scripted fake
  enumerator. Rule: 3.
- Decision: the `EntirelyOpaque` snapshot -> a `lock` body, since its old `foreach` body now lowers.
  `WholeBodyOpaqueKeepsByRefParametersAsOuts` switched from `foreach` to `lock` for the same reason.
- An array `foreach` is not zero-opaque, as the Design expected: the CFG uses the non-generic
  `IEnumerator`, so `Current` is unboxed and the `finally` disposes through `as IDisposable`, and both
  are `Conversion` opaques at those points (test `ForEachOverAnArrayIsOpaqueOnlyAtItsConversions`;
  P1-004 replaces the shape with an index loop).
- Where the CFG converts the enumerator or resource to `IDisposable`, the `Dispose` receiver is a
  `cast.<From>.System.IDisposable` read, whose nullness M3-010 reads from `null.System.IDisposable`
  rather than the operand's. So the `finally` has a `NullReferenceException` branch after the
  `IsNull` test that the real code cannot take. It over-approximates the same way on both sides
  (snapshots `ForEachOverIEnumerableOfInt`, `UsingDeclaration`).
- The `business-layer` census moved: `foreach-enumerator` and `using` are gone,
  `pairsWithoutOpaque` 4 -> 6, `changedPairsWholeBodyOpaque` 1 -> 0. A local `equiv compare` on the
  sample now gives `TotalQuantity` (renamed local) and `Export` (`using`) Equivalent; `Subtotal` keeps
  only its `decimal` `CompoundAssignment` (M4-002), `Record` stays `lock`.
- Locally, the three `webapi-basic` integration tests fail to load the legacy solution
  (`System.Web.Http` unresolved: its packages are not restored in this worktree). That is
  environmental and unrelated; CI restores them.
