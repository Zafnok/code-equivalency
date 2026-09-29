# P1-015 IL fallback, part 2: fields, arrays, addresses, type tests, exceptions and pure operators
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-014

## Goal
The rest of P1-012's mapping table, lowered to the heap, cast, exception and pure-function IR that
`IrLowerer`'s collaborators already produce. After this ticket, every key the spike's report lists
as mapped is `lowered` in `IL-COVERAGE.md`, and the unmapped ones stay opaque by design. About 5
source files and 3 test files.

## Spec references
ADR 0039; VERIFICATION-MODEL.md sections 2 and 3.1; ADR 0015 and ADR 0018 (heap); ADR 0025 (pure
operators); M3-010 and M4-005 (cast and type maps); M4-008 (exception edges and `finally` copies);
P1-006 (array maps keyed by value); P2-017 (null check after the value).

## Design
- **Addresses.** `LdFlda`, `LdsFlda`, `LdElema`, `AddressOf` and `LdLoca` are lowered only as an
  *address operand*, exactly as the spike's table defines it. `LdObj`/`StObj` through a field or
  element address become the `field.<Type>.<Field>` or `array.<Sort>` map read or write that
  `HeapLowerer` emits, with its null and bounds checks in the CLR's order (P2-017). `LdLoca` passed
  to a `ref`/`out` parameter becomes an `IrCall` ref output (M4-003). An address that escapes is
  opaque with key `...[address escapes]`.
- **Arrays.** `NewArr`, `LdLen` and element access use P1-006's value-keyed maps and P2-001's
  `ArrayCreation` lowering.
- **Type tests.** `IsInst`, `CastClass` and `Box` lower to M4-005's `istype.<From>.<To>`
  and `cast.<From>.<To>` reads and the `InvalidCastException` branch. `LdTypeToken` is the shared
  `typeof.<T>` input. `LdFtn` of a named method is the designated constant that names it. A
  `Comp` of a reference against `LdNull` reads the `null.<T>` shadow.
- **Exceptions.** `TryCatch`, `TryCatchHandler`, `TryFinally` and `TryFault` go through
  `ExceptionLowerer`'s edges and copied `finally` blocks. `Throw` of a `NewObj` is `IrThrow` of that
  type. Every other throw, rethrow, and read of a caught exception is opaque.
- **Pure operators.** `BinaryNumericInstruction`, `Comp` and `Conv` on `float`, `double` and
  `decimal` go to `PureCatalogue`'s `f32.*`, `f64.*`, `dec.*` and `conv.*`. `LdcF4`, `LdcF8` and
  `LdcDecimal` go to the constants M4-002 uses. A `decimal` operator arrives as a `Call` to
  `System.Decimal::op_*`, which is mapped through `PureCatalogue` exactly as `IrLowerer` maps the
  `IOperation`. A user-defined operator is a call to `op_*` and becomes its `op:<identity>`.
- **Pitfalls.** ILAst's `ldobj`/`stobj` on a struct-typed address copy the whole value. A struct
  field write through `AddressOf` of a temporary must not reach the original. `DefaultValue` of a
  type parameter stays opaque, and so does any `Conv` to or from a native integer.

## Acceptance criteria (all must hold; nothing beyond them)
1. Every key that `docs/runs/2026-09-28-il-lowering-spike.md`'s mapping table lists is `lowered` in
   `IL-COVERAGE.md`, with a named test. Every key its "ten most frequent unmapped kinds" lists is
   `opaque` with its reason.
2. The IL oracle test from P1-014 also generates field and array reads and writes, `ref` and `out`
   arguments, `as` and a downcast, `try`/`catch`/`finally` with a thrown `new`, and `double` and
   `decimal` arithmetic, and it still agrees with the CLR.
3. For each `samples/` method whose IOperation lowering holds no `IrOpaque`, its IL lowering is
   Equivalent to its IOperation lowering when `Z3Backend` verifies the two as a pair. The test lists
   the methods that are not, each with the one-line reason, and the list is empty or states only
   known representational differences.
4. `IrLowerer` and `IlLowerer` share `HeapLowerer`, `ExceptionLowerer`, `HeapInputs` and
   `PureCatalogue`. Neither has its own copy of a map name or a catalogue key.

## Files
- `src/Equiv.Frontend.CSharp/Lowering/Il/IlLowerer.cs`, `IlHeap.cs`, `IlExceptions.cs`, `IlKeys.cs`
- `src/Equiv.Frontend.CSharp/Lowering/HeapLowerer.cs`, `ExceptionLowerer.cs` (only to take operands that do not come from an `IOperation`)
- `tests/Equiv.Frontend.CSharp.Tests/Lowering/Il/IlLowererTests.cs`, `IlLoweringOracleTests.cs`
- `tests/Equiv.Tests.Integration/IlLoweringParityTests.cs`
- `docs/tickets/IL-COVERAGE.md`

## Tests
- `IlLowererTests.AFieldWriteIsItsMapWrite`, `.AnEscapingAddressIsOpaque`, `.ArrayElementIsNullThenBoundsChecked`,
  `.DowncastThrowsInvalidCast`, `.FinallyIsCopiedOnEveryExit`, `.ACaughtExceptionReadIsOpaque`,
  `.DecimalOperatorIsItsCataloguedFunction`, `.AStructCopyDoesNotAliasItsSource`
- `IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp` (extended)
- `IlLoweringParityTests.IlAndOperationLoweringsOfOpaqueFreeSamplesAreEquivalent`

## Size guard
More than 6 source files changed, or a new IR instruction, means the mapping has drifted from the
spike's table. A new IR construct needs `equiv-extend-ir` and its own ticket.

## Out of scope
Lambdas, closures and local functions. State machines (`async`, iterators). Wiring the fallback into
runs (P1-016).

## Notes
