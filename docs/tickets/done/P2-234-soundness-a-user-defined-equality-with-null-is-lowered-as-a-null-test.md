# P2-234 Soundness bug: a user-defined `==` or `!=` with `null` is lowered as a null test, not as the operator
Status: done (PR #535)
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
The IOperation lowering reads `x == null` and `x != null` as the null shadow of `x` (`IrLowerer.NullTest`, M2-004
criterion 5) before it asks whether the operator is user-defined, so a type's own `operator ==` is never applied. The
compiler calls that operator, and it need not be a null test: a probe on `main` at `8645e40e` lowered
`static bool M(D d) => d == null;`, where `D`'s `operator ==` returns `true`, to `mapread %null.D, %d`, so it would
call `M` and `static bool N(D d) => d is null;` Equivalent while `M` returns `true` for every `d`. A type whose
`==` compares with `null` in its own way (an object that reports itself destroyed, say) is a migration that changes
`== null` to `is null` and changes behaviour.

Found by P2-220, whose sample `runtime-row-call-cannot-reach` added `Type.GetType("Mono.Runtime") != null`:
`IlLowererTests.CallIdentitiesMatchTheOperationLowering` lists that body's two sides as known differences, because
the IOperation lowering reads `null.System.Type` and the IL lowering applies `op:System.Type::op_Inequality`, which
the compiler emits.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (`Binary`), M2-004, M4-002, P2-008, ADR 0039.

## Acceptance criteria (all must hold; nothing beyond them)
1. A comparison with `null` whose operator is user-defined is lowered as that operator (its `op:` function), as any
   other user-defined operator is, unless an entry that cites documentation says the operator is a null test there
   (`System.String`'s, which Roslyn reports with no method, stays a null test).
2. `M` and `N` above are not Equivalent (unit test), and `d == null` with an operator that is a null test still
   proves Equivalent to `d is null` where the operator is the predefined reference comparison.
3. The two `runtime-row-call-cannot-reach` entries leave `IlLowererTests.KnownCalleeDifferences`, or Notes say why
   `System.Type`'s operator is listed as a null test.

## Tests
A lowering test per criterion; `IlLowererTests.CallIdentitiesMatchTheOperationLowering`.

## Size guard
A rewrite of how null shadows are tracked: stop.

## Out of scope
`is null` patterns (P2-008), nullable value types compared with `null` (P2-083).

## Notes
- Filed 2026-10-16 by P2-220.
- Decision: the null test applies when Roslyn reports no `OperatorMethod` (the predefined reference comparison, a
  delegate's, and `string`'s `==`/`!=`, whose `String.Equals(string, string)` is documented as equal when both are `null`
  and not equal when one is) or when the method is declared by `System.Type` (`IrLowerer.NullTestOperatorType`). Every
  other user-defined operator is its `op:` function.
- Criterion 3 takes its second branch: `System.Type`'s operator is listed as a null test because with a `null` operand it
  returns `true` for the same reference and `false` otherwise, before comparing anything, on .NET Framework 4.8 (the
  CLR's internal call `RuntimeTypeHandle::TypeEQ`, open-sourced in dotnet/coreclr `src/vm/runtimehandles.cpp`) and on .NET
  (dotnet/runtime `Type.Helpers.cs`). Without the entry `IrLowererTests.TypeOfIsNeverNull` (`typeof(string) == null` is
  `false`) failed: the operator's `op:` result is unknown. So the two `get_IsMono()` entries stay in
  `IlLowererTests.KnownCalleeDifferences` and `IlLoweringParityTests.Known`, their reason reworded: the IOperation lowering
  reads the shadow because the operator is a null test, and the IL lowering applies it.
- Precision given up: other BCL operators that are null tests too (`Uri`, `Version`, the reflection types) are now their
  `op:` functions, so `uri == null` to `uri is null` is no longer proved. Filed as P2-235 rather than listed here, since
  criterion 1 asks for a cited source per entry.
- `IlLoweringParityTests` could not run locally: the legacy samples fail to load in this worktree (`System` unresolved), a
  machine issue unrelated to the change; CI runs it.
