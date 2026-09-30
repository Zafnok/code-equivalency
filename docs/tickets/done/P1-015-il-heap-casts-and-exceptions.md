# P1-015 IL fallback, part 2: fields, arrays, addresses, type tests, exceptions and pure operators
Status: done (PR #302)
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
- Decision: a struct is a value, its fields maps keyed by it (as `HeapLowerer` keys them), and a write of one field makes a new value, the next of `new.<Sort>` (`HeapLowerer.Fresh`, which `Allocate` now uses too) with that field changed and every other instance field copied, stored where the struct was. Alternatives: the IOperation lowering's write of the field map at the struct's own value. Rule: soundness and the Design's pitfall: the value-keyed write makes `S t = s; t.X = 5;` change `s.X` (the IOperation lowering does so today), so a copy would alias its source (`AStructCopyDoesNotAliasItsSource`).
- Decision: `ExceptionLowerer` gains the members that take regions of any type (`Unwind<TRegion>`, `Raise<TRegion>` over `Clause<TRegion>`, `Copy` and `Filter` keyed by `object`, `Ambiguous`, a public `ThrowBlock`), and its CFG paths go through them; the IL lowering constructs it with no CFG, as it constructs `HeapLowerer` with no callbacks. `HeapLowerer` gains an access's already-read nullness (`Access.IsNull`), an element overload taking it, `Fresh`, and a public `Length`. Rule: criterion 4 and the Files line (only to take operands that do not come from an `IOperation`).
- Decision: ILSpy reads IL's `neg` on floating point as a subtraction from a `+0.0` it makes itself, with no IL range, exactly as it reads C#'s `0.0 - x` but for the range; the IL lowering tells them apart by that range. Rule: soundness: the two differ at `x = 0.0`, so one function for both would prove `-x` and `0.0 - x` equal.
- Decision: an array element's map and value type are the array's own element type, not the `ldelema`'s, which ILSpy takes from IL's opcode (`short` for a `char[]`'s `stelem.i2`, `byte` for a `bool[]`'s `ldelem.u1`). Rule: criterion 3: `api-drift`'s legacy `Parts` wrote its `char[]` through a `short[]` map and failed its bounds check.
- Decision: a `callvirt` names the override the receiver's static type binds; Roslyn's IL names the least-derived method (`System.Object::ToString` for a `StringWriter`). Rule: criterion 3 and ADR 0039's "the same identity": the IOperation lowering names the override C# binds.
- Decision: a reference receiver of an interface's method is converted to the interface through its `cast` map, as C#'s conversion is in the IOperation lowering (a `using`'s `IDisposable`); its null check reads the receiver's own shadow. Rule: precision; the IOperation lowering reads `null.System.IDisposable` of the conversion, which `IlLoweringParityTests` lists as a representational difference.
- Decision: a type test whose operand already converts implicitly to the tested type passes on every non-null operand, with no `istype` read. Rule: Roslyn's optimiser removes the `object` local of `object o = box; (Box)o`, leaving a `castclass Box` of a `Box`, and an unconstrained `istype.Box.Box` made the oracle throw where C# does not.
- Decision: a store of a caught exception into a local (ILSpy's copy of the handler's variable) stores nothing, and a fragment never reads a caught exception. Rule: the store read a variable no store reaches, which `SsaBuilder` hoists into the entry as an `undefined` opaque that every run reaches.
- Decision: `decimal`'s static `Zero`, `One`, `MinusOne`, `MaxValue` and `MinValue`, which Roslyn reads for those literals, are the constants the IOperation lowering gives the literals; every other operator `System.Decimal` declares is catalogued, and Roslyn never calls `op_UnaryPlus` (both pinned by `FloatingPointAndDecimalAreTheOperationLoweringsFunctions`).
- Decision: a narrow integer converted to floating point is converted from its stack type (`conv.i32.f64` for a `byte`), the IOperation lowering's `conv.u8.f64` being the same value under another uninterpreted name; and a floating-point `!=` that ILSpy reads, in branch form, as the other arm of an `==` is `eq`. Both are representational differences from the IOperation lowering; both sides of a pair the fallback lowers get the same one.
- Decision: a struct's constructor is lowered only as a `newobj`: ILSpy reads one called on a local's address as the local's store of a `newobj`, and Roslyn stores one into a field, element or static field with `newobj` then `stobj` (checked); `TryFault` takes `TryFinally`'s path on the exception route only, since no C# a symbol names compiles to a `fault` block (Roslyn emits them only in an iterator's `MoveNext`); `ldtypetoken` is lowered only as `Type.GetTypeFromHandle`'s lone argument, the only place C# puts one.
- Decision: `IlLoweringParityTests` loads each sample side as a run does (`MsBuildSolutionLoader`) and verifies with the default bound and timeout; its known list is the two `ConfirmAsync` (an async method's IL is its kickoff), the two `Export` (`using` resource, above) and the two `Reserve` (the IOperation lowering reads `order != null` through `null.System.Object` of the conversion to `object`). Every other opaque-free sample method is proved.
- Decision: P1-014's `CallIdentitiesMatchTheOperationLowering` stays pinned to P1-014's keys; the heap, casts and exceptions are compared by criterion 3's Z3 parity instead, which is stronger. The IL oracle's generator puts no `catch` region inside another's `try` block: a call's exception of no known type that two could take is `call-throw-in-try` in either lowering.
- Deviation: the partial class's files are `Lowering/Il/IlLowerer.Heap.cs` and `IlLowerer.Exceptions.cs`, not `IlHeap.cs` and `IlExceptions.cs`: MA0048 requires a partial type's file to be named for the type. `IlAstReaderTests.cs` changes too: four of its tests used a field read as the opaque whose span they check, and a field read is now lowered, so they use `~`.
- Note for P1-016: the IL lowering has no legacy side, so `PureCatalogue.Entry.RuntimeSensitive` is asked with `x87: false`; the run that picks the fallback should pass the legacy side's x87 flag as `IrLowerer.Lower`'s catalogue does.
