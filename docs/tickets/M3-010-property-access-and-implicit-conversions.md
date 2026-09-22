# M3-010 Property access as accessor calls; implicit reference and boxing conversions
Status: todo
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
