# M4-002 `IrPure`: floating-point, decimal and user-defined operators as shared functions
Status: done (PR #194)
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
- Decision: runtime-sensitivity travels on the instruction as `IrPure.RuntimeSensitive`, an `init` property beside criterion 1's positional shape (as `IrOpaque.WholeBody` is), spelled with a `!` after the function in the text format, as a runtime-changed callee is. Alternatives: an `old.`/`new.` prefix chosen by the frontend (the lowerer of one side does not name the pair), a name pattern the encoder recognises (the encoder cannot see the x87 platform). Rule: 1.
- Decision: a function is side-specific in a pair when any application of it in either procedure is runtime-sensitive, and then every application on both sides uses `old.`/`new.`. An x87 legacy `f64.add` therefore faces `new.f64.add`, the pair ADR 0025 names, though only the legacy side knows its platform. Alternatives: prefix only the flagged applications (the modern side would keep the shared name). Rule: 1.
- Decision: `IPureOracle.Answer(IrPure pure, ImmutableArray<IrValue> arguments)` returns `IrPureResult(IrValue Value, ImmutableArray<bool> Threw)`. It is given the instruction, so the model oracle reads the function, its runtime-sensitivity, its target type and its exception types from it. It is the optional last parameter `pure` of `IrInterpreter.Run`, so every existing caller is unchanged; a run that reaches an `IrPure` without one throws `InvalidOperationException`. Alternatives: extend `ICallOracle` (every call oracle would have to answer pure functions), a type test on the call oracle. Rule: 4.
- Decision: with a taint predicate, every `IrPure` result and flag is tainted, whatever the predicate says of calls, and the run's source is `CallIdentity(<function>)`, so `properties.abstractions` names the function (`dec.mul`), with no span (an `IrPure` has none, as an `IrCall` has none). Without a predicate nothing is tainted, so `IrTaint.None` still means "no predicate". Alternatives: ask the predicate with a `pure:` identity (every caller would have to know the prefix). Rule: 1 (criterion 2).
- Decision: Z3 names are `pure:<name>(<argument sorts>)-><result sort>` and `pure.threw:<name>(<argument sorts>):<exception type>`, with no position argument. A flag is keyed by its exception's type, since the IR carries only the type; the catalogue's `throw.divzero` and `throw.overflow` are those types. Alternatives: short flag names in the IR. Rule: 1.
- Decision: `PureCatalogue` (frontend, `internal static`) generates its 96 entries from small tables: the operators of `f32`, `f64` and `dec`, and every numeric conversion between `i8 u8 i16 u16 char i32 u32 i64 u64` and `f32 f64 dec`, and `f32`/`f64`/`dec` among themselves. Types are Roslyn `SpecialType`s. `Entries` lists them by name, and a test pins the count and spot entries. Alternatives: a hand-written list of 96 rows. Rule: 3.
- Decision: a checked and an unchecked conversion share one function name. The entry lists the unchecked exceptions (`Throws`) and the checked ones (`CheckedThrows`), which differ only for floating point to an integral type. That is sound because a checked conversion that does not throw yields the unchecked value, so one result function serves both. Alternatives: a `.checked` name (legacy unchecked and modern checked would never agree even in range). Rule: 1.
- Decision: `dec.rem` has an overflow flag as well as a divide-by-zero flag, because `Decimal.Remainder` documents `OverflowException`. A conversion from `decimal` to an integral type throws on overflow in any context, as C# does. A conversion from floating point to `decimal` always has the overflow flag (NaN, infinities, out of range). A flag for an exception that never happens costs nothing, since both sides share it.
- Decision: a user-defined operator or conversion is `op:<call identity>` (the `CallIdentityFactory` identity, so the rename map applies), with one flag of type `System.Exception` routed as an opaque call's `threw` is (`known: false`), because such an operator can throw anything. It is runtime-sensitive when its identity is runtime-changed. It is used only when the operands and result are exactly the method's parameter and return types; a lifted operator (`Money? + Money?`, `Money? < Money?`) or conversion is opaque with the operation's kind, as before. Alternatives: no flag (an operator that throws would be lost). Rule: 1.
- Decision: unary `+` on `float`, `double` and `decimal` is its operand, as it is on integers. Rule: 4.
- Decision: the x87 flag reaches the lowerer as a new optional `legacy` parameter of the equivalence-taking `IrLowerer.Lower` overload, which `CSharpFrontend.LowerWithIrLowerer` passes. The platform rule is M3-015's (`Platform.X86` or `AnyCpu32BitPreferred`), in `PureCatalogue.IsX87`, and on such a legacy side every function that takes or yields `float` or `double` is runtime-sensitive, comparisons and conversions included. `BoundSerialiser` keeps its own copy of the rule; `Fingerprinting/*` is not in this ticket's files. Rule: 4.
- Decision: the lowering oracle's `decimal` case adds a `decimal m` parameter to every generated method and `decimal` expressions (`m` and `(decimal)` of an `int`, under `+ - * / %`) converted back with `(int)` or compared. The test's `DecimalOracle` answers the IR's pure functions with `System.Decimal`'s own operators, one sort element per value bit for bit, raising the flag of the exception the operator throws. `m` ranges over edges including `decimal.MaxValue`, `MinValue` and `1e-28`, so overflow and divide-by-zero are both exercised; the test asserts the lowered IR reaches `conv.i32.dec`, `conv.dec.i32`, `dec.mul`, `dec.div` and `dec.lt`. Rule: 3.
- Decision: criterion 3 names binary, unary and comparison operators and conversions, so compound assignment (`+=`) and `++`/`--` on `float`, `double` and `decimal` stay opaque (`CompoundAssignment`, `Increment`). The business-layer README says so for `Subtotal`. Rule: ticket scope.
- Decision: test names. `FloatToIntConversionIsSideSpecific` is the backend test (both sides' `conv.f64.i32` become `old.`/`new.` functions and the pair is not Equivalent). The frontend's is `AFloatToIntConversionIsMarkedRuntimeSensitive`. `X87LegacyFloatIsSideSpecific` is the frontend test that marks an x87 legacy side, and `AFunctionOneSideMarksRuntimeSensitiveIsSideSpecificOnBothSides` is its backend half. `PureAddsNoTraceEvent` exists in Core (the interpreter) and in the frontend (a lowered `decimal` expression). `SwappedOperandsAreUnknownAbstraction` exists at the IR level and, as `SwappedDoubleOperandsAreUnknownAbstraction`, end to end. The text-format round trip is `IrTextTests.DumpWritesEveryConstructInTheDocumentedSpelling` (two `pure` lines), `IrPureTests.TheRuntimeSensitiveFlagRoundTripsInIrText`, and the CsCheck round trip over the generator, which now emits `IrPure`. Rule: 3.
- Surprise: Roslyn binds `string == string` and `!=` as predefined operators with no `OperatorMethod`, although they are `System.String`'s user-defined `op_Equality` and `op_Inequality`. The lowerer resolves that method itself. `==` on `double` must never be an `IrBinary` equality of sort elements, since `NaN != NaN`; it is `f64.eq`.
- Surprise: a user-defined conversion with a standard conversion after it (`(long)money`, with `explicit operator int(Money)`) reaches the lowerer as two nested `IConversionOperation`s, so the user-defined one is exact and the widening an ordinary `sext`.
- Surprise: `CompareCommand`'s `assumedCallees` (M3-015 criterion 9) lists `IrCall` callees only, so a user-defined operator that is itself a matched pair is assumed equivalent without being listed. Left as is: this ticket's files do not include `Equiv.Cli`.
- Note: an `op:` function treats a user-defined operator as independent of the heap. That is ADR 0025's decision, and the same gap calls have until P1-005 (VERIFICATION-MODEL section 2).
- Note: locally on Linux, every integration test that loads a sample through MSBuild fails, because the legacy side needs the .NET Framework 4.8 reference assemblies (M3-029), as the M3-016 notes found for `webapi-basic`. The business-layer census snapshot was therefore edited by hand. It was checked by compiling the sample's sources in memory and lowering and verifying every `OrderService` method: `LineTotal` has no opaque and is Equivalent, `RoundTotal` stays Divergent, and no `Binary` or `Conversion` opaque remains. `PureOperatorTests.BusinessLayerLineTotalIsEquivalent` and the census snapshot run on the Windows `gates` leg.
