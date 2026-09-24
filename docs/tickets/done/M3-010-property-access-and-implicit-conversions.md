# M3-010 Property access as accessor calls; implicit reference and boxing conversions
Status: done (PR #159)
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
Today every property access except an array's `Length` is `IrOpaque("PropertyReference")`, so
almost every real C# method is Unknown. The same goes for any implicit upcast or boxing
(`IrOpaque("Conversion")`). After this ticket, a property read or write lowers exactly as a call
to its accessor does. An implicit reference or boxing conversion lowers to a read of a synthesised
map that acts as an uninterpreted, trace-free function. Both lowerings are sound only because
M3-001 keys call results by trace position (ADR 0018): without that, `r.Current` read twice would
be forced equal.

## Spec references
VERIFICATION-MODEL.md sections 2 (synthesised inputs) and 3; ADR 0014; ADR 0018;
`docs/tickets/IOPERATION-COVERAGE.md` (`PropertyReference`, `Conversion`, `CompoundAssignment`,
`Increment` rows); the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. A property read (`IPropertyReferenceOperation` as a value) lowers to the same shape the
   lowerer emits for an `Invocation` of `property.Property.GetMethod`: the receiver null check,
   `IrCall` with the accessor's `CallIdentity` and the receiver (if any) followed by the index
   arguments (for indexers), and the `threw` edge. An array's `Length` keeps its M2-004
   lowering.
2. A property write (the target of `SimpleAssignment`) lowers as a call to `SetMethod` with the
   receiver, the index arguments and the value, with no target var. `CompoundAssignment`,
   `Increment` and `Decrement` on a property lower as a getter call, the operation, and a setter
   call, with the same checked-arithmetic edges as on a local.
3. A property with no accessor for the access (for example an init-only setter outside an
   initializer) stays `IrOpaque("PropertyReference")`.
4. An implicit reference conversion (upcast to a base class or interface) or a boxing conversion
   whose source and target map to different IR types lowers to
   `IrMapRead(result, cast.<From>.<To>, operand)`, where `cast.<From>.<To>` is a new `In` input in
   `HeapInputs` of type `Map(<from type>, <to sort>)`. It adds no trace event. The nullness of the
   result is read, as for any value, from `null.<To>`. It is not tied to the operand's nullness.
   That over-approximation is stated in the `HeapInputs` doc comment. Every other conversion keeps
   its current lowering.
5. VERIFICATION-MODEL section 2's synthesised-inputs paragraph lists `cast.<From>.<To>`, and
   `IOPERATION-COVERAGE.md` updates the four rows named under Spec references, with test names.
6. `LoweringOracleGen` gains a case that reads and writes a static `int` auto-property, and the
   oracle passes. The oracle's call oracle answers accessor calls from the compiled run.
7. Snapshot tests: instance property read, static property write, indexer read, compound
   assignment to a property, and boxing an `int` into `object`.

## Files
`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (or the collaborator P1-003 extracts, if it has
landed), `src/Equiv.Frontend.CSharp/Lowering/HeapInputs.cs`, `docs/VERIFICATION-MODEL.md`,
`docs/tickets/IOPERATION-COVERAGE.md`, `tests/Equiv.Frontend.CSharp.Tests/Lowering/*`,
`tests/Equiv.TestSupport/LoweringOracleGen.cs`.

## Tests
`PropertyReadIsACallToTheGetter`, `PropertyWriteIsACallToTheSetter`,
`CompoundAssignmentToAPropertyGetsOperatesAndSets`, `IndexerReadPassesTheIndex`,
`InitOnlySetterOutsideInitializerIsOpaque`, `UpcastIsAReadOfTheCastMap`,
`BoxingIsAReadOfTheCastMap`, `CastMapAddsNoTraceEvent`, the five snapshots, and the extended
oracle.

## Size guard
No `Equiv.Core` change. If the lowerer grows by more than about 250 lines, stop: property access
should reuse the `Invocation` path, not copy it.

## Out of scope
Explicit (downcast) conversions, `as`, `is` type patterns, user-defined conversions, unboxing.
Inlining auto-property bodies as field accesses. Object and collection initializers. `foreach`,
`using` and constructors (M4-001).

## Notes
- Decision: accessor calls share one path, `IrLowerer.Accessor` over `Operands` (receiver, null check,
  arguments), which `Invoke` now uses too. A compound assignment, `++` or `--` on a property evaluates
  the receiver and index arguments once and passes them to both accessors.
- Decision: a compound assignment now evaluates its right operand after reading the target, which is
  C#'s order. For locals this only renumbers temps in four existing snapshots (`CompoundAssignment`,
  `IncrementAndDecrement`, `ForLoop`, `DoWhileLoop`). For a property it puts the getter call before
  any call in the right operand.
- Decision: when the value branches, the CFG captures an assigned property before the value. Such a
  capture (found by scanning the CFG for assignments whose target is a capture reference) evaluates
  only the receiver and index arguments; the accessor runs at the assignment. Without this the
  capture called the getter and the assignment was opaque. The oracle found it on `P = c ? a : b`.
- Decision: the oracle's call oracle (`LoweringOracleTests.AutoPropertyOracle`) answers `get_P` and
  `set_P` the way `P`'s backing field would. Both runs start `P` from the input's `B`. The compiled
  run resets `P` by reflection before each call.
- Observation (criterion 3): code that writes an init-only setter outside an initializer does not
  compile, and Roslyn binds that access, like a read of a property with no getter, as `Invalid`. The
  cases that bind are an init-only setter in an object initializer and a write, `++` or `+=` to a
  ref-returning property, which has no setter. `InitOnlySetterOutsideInitializerIsOpaque` uses those.
  So a no-getter read cannot occur in bound code, and `Accessor` takes the no-accessor branch only
  for setters.
- Observation: the null literal's conversion is a reference conversion with no source type, so
  `IsCast` checks the operand's type.
- Observation: `LoweringCensusTests.BusinessLayerCensusSnapshot` (Windows-only) changes. The legacy
  side needs the .NET Framework 4.8 reference assemblies, which the Linux dev box does not have, so
  the new census was computed with `compare --lower-only` using the modern solution on both sides
  (on `main` this reproduces the committed snapshot exactly). A direct Roslyn lowering of both
  sides' sources gave the same opaque reasons on each side. Changes: `PropertyReference` 3 → 0,
  `Conversion` 3 → 1, pairs without opaque 2 → 4. Before P2-009 merged, `undefined` also went
  1 → 2, because `QuantityOf` read `line` from the still-opaque pattern `item is OrderLine line`;
  P2-009 defines such variables, so no `undefined` remains.
- Merge with main (P2-009, M3-030): M3-030 added changed-pair keys to the census. A probe that
  lowers both sides' sample files with Roslyn and pairs methods by identity reproduced main's
  snapshot exactly, before and after the merge. On the merged code the four changed pairs are
  RoundTotal and Reserve (no opaque), LineTotal (`Binary+Conversion`) and TotalQuantity
  (`foreach-enumerator`): `changedPairsWithoutOpaque` 1 → 2, and `changedReasonSets` lose
  `PropertyReference` and the lone `Conversion`.
- Observation, not fixed here: an opaque `Conversion` does not lower its operand. So in `LineTotal`,
  `(decimal)line.Quantity` swallows the `get_Quantity` call. The pair is Unknown either way.
- Observation: `Length` on an array that is not a plain variable (`a[0].Length`), and a setter in an
  object initializer, are now accessor calls. The initializer's implicit receiver is still an opaque
  `InstanceReference`; object initializers are out of scope.
