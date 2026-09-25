# M4-002 `IrPure`: floating-point, decimal and user-defined operators as shared functions
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-016, M3-015

## Goal
ADR 0025. `decimal` and `double` arithmetic, comparisons and conversions, and every user-defined
operator, including `string ==`, are opaque today. After this ticket they lower to `IrPure`
applications of functions that both sides share, with exact exception edges and no trace event.
Runtime-sensitive functions are side-specific.

## Spec references
ADR 0025; ADR 0026; ADR 0013 (width conventions); VERIFICATION-MODEL sections 2, 3 and 5; the
`equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. `IrPure(IrVar Target, ImmutableArray<IrPureThrow> Throws, string Function, ImmutableArray<IrVar> Args)`
   exists in `Equiv.Core.Ir`. Each `IrPureThrow(IrVar Flag, string ExceptionType)` is one Bool
   output. It is supported by the validator, the text format, equality, the visitor and the
   generators.
2. `IrInterpreter` evaluates `IrPure` through an `IPureOracle`. In replay the oracle answers from
   the model. Every `IrPure` result and flag is tainted (M3-016).
3. The frontend lowers these to `IrPure`, each flag branching to an `IrThrow` of its exact type:
   - binary, unary and comparison operators on `float` and `double`
     (`f32.<op>`, `f64.<op>`; no throws);
   - `decimal` operators (`dec.<op>`, with `throw.divzero` for `/` and `%`, and `throw.overflow`
     where the BCL documents `OverflowException`);
   - conversions to and from floating point and `decimal` (`conv.<from>.<to>`, with
     `throw.overflow` when checked or when the BCL documents it);
   - every user-defined operator and user-defined conversion (`op:<normalised identity>`).
4. The function catalogue is one internal static class listing each name, its argument and result
   types, and its exceptions. Anything not in the catalogue stays opaque with its current reason.
5. `ProductEncoder` declares one Z3 function per name, and one Bool function per flag, shared by
   both sides. For floating-point to integer conversions, and for floating-point arithmetic when
   M3-015 marks the legacy project as x87, it declares `old.` and `new.` prefixed functions instead.
6. A `try { a * b } catch (OverflowException) { return 0; }` on `decimal` against plain `a * b` is
   not Equivalent. Test `OverflowCatchOnDecimalIsNotEquivalent`.
7. `IOPERATION-COVERAGE.md` rows `Binary`, `Unary` and `Conversion` updated with test names.
   On `business-layer`, the changed method that calls `decimal` arithmetic without changing it is
   decided. README and census updated.

## Files
`src/Equiv.Core/Ir/IrPure*.cs`, `src/Equiv.Core/Ir/IrInterpreter.cs` and the IR infrastructure,
`src/Equiv.Frontend.CSharp/Lowering/*`, `src/Equiv.Verify.Z3/*`, `tests/**`,
`docs/tickets/IOPERATION-COVERAGE.md`, `samples/business-layer/README.md`.

## Tests
`DecimalAdditionIsASharedPureFunction`, `DecimalDivisionThrowsDivideByZeroExactly`,
`DoubleComparisonIsPure`, `StringEqualityIsTheUserDefinedOperator`, `PureAddsNoTraceEvent`,
`SwappedOperandsAreUnknownAbstraction`, `FloatToIntConversionIsSideSpecific`,
`X87LegacyFloatIsSideSpecific`, `OverflowCatchOnDecimalIsNotEquivalent`, the text-format round trip,
and the lowering oracle extended with a `decimal` case.

## Size guard
No IEEE-754 or decimal semantics in Z3. If they seem necessary, stop and propose an ADR.

## Out of scope
Math library calls (`Math.Round` and the like stay `IrCall`s). String theory. `checked` floating
point (it does not exist in C#).

## Notes
