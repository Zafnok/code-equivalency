# M4-005 Type tests, `as` and downcasts
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-010

## Goal
`x is T t`, `x as T`, `(T)x` and type patterns in `switch` are opaque today (`switch-pattern`,
`Conversion`). After this ticket, the dynamic type test is a read of a synthesised `In` map
`istype.<From>.<T>`, and the converted value reuses M3-010's `cast.<From>.<T>` map. A failing
downcast branches to `IrThrow("System.InvalidCastException")`.

## Spec references
VERIFICATION-MODEL section 2 (synthesised inputs); ADR 0021 (naming); M3-010 (cast maps); the
`equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. `HeapInputs` gains `istype.<From>.<To>`, an `In` input of type `Map(<from sort>, Bool)`. Its doc
   comment states that it is a free predicate per pair of types, shared by both sides by name.
2. `x is T` reads `istype` for a non-null `x` and is false for null. `x is T t` also binds `t`
   through the cast map. `x as T` is the cast when the test passes and `null` otherwise. `(T)x`
   branches to `InvalidCastException` when the test fails on a non-null `x`, and passes null
   through for a reference `T`.
3. Unboxing to a value type, and a conversion between two types known to be unrelated, stay opaque.
   Type patterns in `switch` statements and expressions lower through the same test.
4. Coverage table rows `IsPattern`, `IsType`, `DeclarationPattern`, `TypePattern` and `Conversion`
   are updated. On `business-layer`, the `is T t` method lowers with no opaque. README and census
   updated.

## Files
`src/Equiv.Frontend.CSharp/Lowering/*`, `src/Equiv.Frontend.CSharp/Lowering/HeapInputs.cs`,
`docs/VERIFICATION-MODEL.md` (section 2's synthesised-input list, as M3-010 does for `cast.*`), `docs/tickets/IOPERATION-COVERAGE.md`, `tests/**`.

## Tests
`IsTypeReadsTheTypeMap`, `IsNullIsFalse`, `DeclarationPatternBindsThroughTheCastMap`,
`AsYieldsNullWhenTheTestFails`, `FailingDowncastThrowsInvalidCast`, `SwitchOnTypePatterns`
(snapshot), `UnboxingStaysOpaque`.

## Size guard
No `Equiv.Core` change. About 150 lines of lowering at most.

## Out of scope
Recursive and property patterns, list patterns, generic type tests on open type parameters.

## Notes
