# P2-235 Precision: a BCL `==` or `!=` with `null` that is a null test is lowered as its operator
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-234

## Goal
P2-234 lowers a comparison with `null` under a user-defined `==` or `!=` as the operator's `op:` function, because the
operator need not be a null test, and keeps it a null test only for `System.Type` (`IrLowerer.NullTestOperatorType`),
whose operators the CLR and dotnet/runtime sources show return `true` for the same reference and `false` when one
operand is `null`. Other BCL operators do the same before they compare anything else, on .NET Framework 4.8 and on
.NET, so `uri == null`, `version != null` or `method == null` migrated to `is null` or `is not null` is now not proved
Equivalent although it is: `System.Uri`, `System.Version` and the reflection types (`System.Reflection.MemberInfo`,
`MethodInfo`, `MethodBase`, `ConstructorInfo`, `FieldInfo`, `PropertyInfo`, `EventInfo`, `Assembly`,
`Module`) are candidates.

## Spec references
P2-234, `docs/tickets/IOPERATION-COVERAGE.md` (`Binary`), M4-002.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each BCL type whose `==` and `!=` are null tests with a `null` operand on both .NET Framework 4.8 and .NET, by a cited
   source (documentation, Reference Source or dotnet/runtime), is listed beside `System.Type`, the citation in the entry.
   A type whose operator does anything else first with a `null` operand is not listed.
2. `x == null` under a listed type's operator proves Equivalent to `x is null` (unit test per type), and `D`'s from
   P2-234's `UserDefinedEqualityEquivalenceTests` still does not.

## Tests
A lowering test per listed type; an equivalence test as in `UserDefinedEqualityEquivalenceTests`.

## Size guard
A type listed without a source that shows its operator with a `null` operand: stop.

## Out of scope
User-defined operators in the code under comparison (they stay their `op:` functions); the IL lowering, which applies
the operator the compiler calls.

## Notes
- Filed 2026-10-16 by P2-234, which kept only `System.Type` (one existing test, `TypeOfIsNeverNull`, and P2-220's sample
  need it) and recorded the rest as precision it gives up.
