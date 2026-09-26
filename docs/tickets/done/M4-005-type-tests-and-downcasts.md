# M4-005 Type tests, `as` and downcasts
Status: done (PR #209)
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
- Decision: a test is modelled when both types are reference types, neither is a type parameter, and
  `Compilation.ClassifyConversion` finds a reference or identity conversion between them. That is the reading of
  "known to be unrelated": no reference conversion exists (such as two sealed classes, or a pair only a user-defined
  conversion relates). Such a test is opaque (`IsType`, `switch-pattern` or `Conversion`), not constant false.
- Decision: IR has no select, so `x as T`, and a downcast of a value that may be null, choose between the cast and
  `null` (element 0, as the `null` literal lowers) with a branch and a join on a fresh `$select<n>` variable. A null
  `(T)x` is `null`, not the cast of `x`, so `(T)x` and `x as T` agree whenever the test passes or `x` is null.
- Decision: `x as T`'s nullness is the negation of the test, recorded when it is lowered and read by `Nullness`. That
  function no longer unwraps an `as` to its operand's shadow, which was wrong even when the `as` stayed opaque. A
  downcast keeps its operand's nullness.
- Decision: a `var` declaration pattern (`MatchesNull`) stays `switch-pattern`: it is not a type test.
- Observation: an `as` whose conversion is implicit (`s as object`) is still an M3-010 upcast, because `IsCast` runs
  first. That over-approximates its nullness as M3-010 already does; it does not read `istype`.
- Observation: `samples/business-layer/expected.sarif.json` (M3-003) embeds the lowering census too, so it changes with
  `BusinessLayerCensusSnapshot`: `pairsWithoutOpaque` 7 to 8, and `switch-pattern` is gone. `QuantityOf` was already
  Equivalent by congruence; its verdict does not change.
