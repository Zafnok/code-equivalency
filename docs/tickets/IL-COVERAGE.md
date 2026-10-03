# IL lowering coverage

Maintained by the IL-fallback tickets (ADR 0039; P1-014 onward). One row per ILAst key: an ILSpy 11 `OpCode`,
refined by a bracketed context where one opcode is lowerable in one position and not in another (`IlKeys.Key`, the
spelling of P1-012's `MappingTable.Key`). Status: `lowered` (with test name), `opaque` (an `IrOpaque` whose reason is
the key), `n/a` (only a transform P1-012's pipeline leaves out produces it; if one ever appears it is opaque with its
key). A key's own row says when a lowered key is still opaque for its operands' types: that opaque's reason is the
key too, and its operands are lowered first, each unmapped one an opaque of its own key
(`IlLowererTests.RefusedInstructionsLowerTheirOperands`). Every key has a row here
(`IlLowererTests.EveryKeyOfTheTableHasARow`).

Shared fragments (ADR 0024 decision 2, as `IOPERATION-COVERAGE.md` describes them for IOperation): an opaque of an
unmapped key carries a fingerprint, the SHA-256 of its ILAst text with every member and type spelled in full and every
local numbered by first appearance, and is shared as one call `opaque:<fingerprint>` over the locals it reads, each
reference's null shadow after it, with a `threw` edge to `System.Exception` and the heap pairs a call has, when the same
fingerprint occurs on both sides (`IlLowererTests.AnOpaqueThatOnlyReadsLocalsIsAFingerprintedFragment`). It gets none when it writes a local, takes a local's address, reads through a `ref`
or a variable whose type the IR has no sort for, branches, cannot complete (a `throw`, a region every path of which
leaves), or calls a member that does not resolve or that `runtime-changes.json` names (M3-015). An opaque of a lowered
key refused for its operands' types, or one whose value no conversion takes to the type its consumer needs, is never
shared.

Types, members and identities: every type and method the ILAst names is resolved to the loaded compilation's symbol by
its documentation ID (`IlSymbols`), and `TypeMapper`, `CallIdentityFactory` and `HeapInputs` run on it as they do for the
IOperation lowering. One that does not resolve (a `ref` or function-pointer type, a member of a reference that did not
load) makes its instruction refused. Values follow IL's stack: a `bool` stays Bool, a narrower integer is extended by
its own type's sign where it is used as an `int`, and a reference passed where an implicit reference conversion takes it
reads `cast.<From>.<To>` (M3-010). Locals start at their type's default (`.locals init`); a source-declared reference
local, a parameter and a stack slot have a null shadow, a compiler-added local (Debug's return temporary) has none, as
the IOperation lowering has no variable for it.

| ILAst key | Status | Test | Ticket |
|---|---|---|---|
| ILFunction | lowered: the procedure: the C# signature (`IrLowerer.Signature`, ADR 0021's names), then the heap inputs used, by name; the body is its `BlockContainer` | IlLowererTests.EverySampleMethodLowersToValidIr, .CallIdentitiesMatchTheOperationLowering | P1-014 |
| BlockContainer | lowered: its blocks, entered at its entry point; the function's, a loop and a switch container alike; a `leave` of a nested one goes on to what follows it | IlLowererTests.LoopsAndNestedContainersAgreeWithTheInterpreter; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| Block | lowered: its instructions in order, a container's block as an `IrBlock` and an `if` arm's inline; only control-flow blocks occur, since the pipeline leaves out the transforms that build initialiser blocks | IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| Nop | lowered: nothing | IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| Branch | lowered: `IrGoto`, to a block of its own container or an enclosing one, through a copy of each `finally` it leaves | IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014, P1-015 |
| Leave | lowered: from the function's container `IrReturn` of its value as the return type; from a nested one `IrGoto` to what follows it; either through a copy of each `finally` it leaves, after its value; from a `when` filter's container the branch to the handler when its value holds and on to the next clause when it does not | IlLowererTests.LoopsAndNestedContainersAgreeWithTheInterpreter; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp, IlLowererTests.FinallyIsCopiedOnEveryExit, .AFilterDeclinesToTheNextHandler | P1-014, P1-015 |
| IfInstruction | lowered: `IrBranch` on its condition as Bool; with a value, as ILSpy inlines `&&` and `||` into an expression, each arm the value of the type wanted and a join | IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| SwitchInstruction | lowered: `IrSwitch` on an `int` or `long` value, one case per label of every section but the default, each label the value's own width's bits (ILSpy's labels are the sign-extended value, `uint` included); each section's body is lowered in a block of its own | IlLowererTests.ASwitchIsOneIrSwitch; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| SwitchSection | lowered: a case of its `IrSwitch`, or its default | IlLowererTests.ASwitchIsOneIrSwitch | P1-014 |
| LdLoc | lowered: the variable's SSA value; `this` of a class is the receiver input, as the IOperation lowering reads it where referenced; a `ref`, `out` or `in` parameter and a slot holding an address are, as an address, their place; opaque when the variable's type has no sort (a struct's `this`, a pointer, a compiler-generated closure class), or when its value is used as a type no conversion takes it to (an enum as an integer) | IlLowererTests.RefusedInstructionsLowerTheirOperands, .AnAddressIsItsPlace; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014, P1-015 |
| LdLoc[caught exception] | opaque: the caught exception object is not modelled, as `CaughtException` is not in IOperation lowering | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| StLoc | lowered: an SSA store of its value as the variable's type, and of the value's nullness to its shadow; to a slot that holds an address, the place it names, evaluated there; to a variable holding a caught exception, which only copies another, nothing; opaque, its value lowered first, when the variable's type has no sort | IlLowererTests.RefusedInstructionsLowerTheirOperands, .AnAddressIsItsPlace; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014, P1-015 |
| StLoc[ref local] | opaque: a `ref` local, as IOperation lowering leaves it | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| LdcI4 | lowered: `IrConst` of the type wanted: that width's bits, Bool (not zero), or an enum's element as `TypeMapper.Constant` spells its underlying value | IlLowererTests.ConstantsTakeTheTypeTheyAreUsedAs; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| LdcI8 | lowered: as `LdcI4` | IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| LdStr | lowered: the `System.String` element `TypeMapper.Constant` gives the text, the IOperation lowering's element for the same literal | IlLowererTests.ConstantsTakeTheTypeTheyAreUsedAs | P1-014 |
| LdNull | lowered: element 0 of the sort wanted | IlLowererTests.ConstantsTakeTheTypeTheyAreUsedAs | P1-014 |
| BinaryNumericInstruction | lowered: `IrBinary` on `int` or `long` operands, the instruction's `Sign`, not the operands' C# types, choosing `sdiv`/`udiv`, `srem`/`urem`, `ashr`/`lshr` and the overflow check; a zero divisor throws `System.DivideByZeroException` and, signed, `MinValue / -1` `System.OverflowException`, unchecked too; `add`, `sub` and `mul` with overflow checking throw `System.OverflowException`; a shift count is masked to the width, as `IrLowerer` shifts; `&`, `|` and `^` of two `bool` values are Bool; on `float` and `double` `PureCatalogue`'s `f32.*` and `f64.*`, IL's `neg` (which ILSpy reads as a subtraction from a `0.0` it makes, with no IL range) being `neg` and C#'s `0.0 - x` a subtraction; native-integer operands are opaque | IlLowererTests.UnsignedDivisionFollowsTheInstructionSign, .FloatingPointAndDecimalAreTheOperationLoweringsFunctions; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014, P1-015 |
| Comp | lowered: an `IrBinary` comparison of `int` or `long` operands by its `Sign`, or an equality of two values one of which is a `bool`, as Bool; on `float` and `double` the ordered comparison's `f32.*` or `f64.*` function (ILSpy reads IL's unordered comparisons as the negation of an ordered one; one left unordered is opaque); a reference against `null`, which ILSpy puts on the right, reads the reference's null shadow (P2-017); a comparison of two references or of native integers is opaque | IlLowererTests.RefusedInstructionsLowerTheirOperands, .FloatingPointAndDecimalAreTheOperationLoweringsFunctions, .DowncastThrowsInvalidCast; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014, P1-015 |
| LogicNot | n/a: ILSpy 11 has no such instruction; `!b` is a `Comp` of `b` against 0, lowered as `Comp` | IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| Conv | lowered: between integral types: extension by the conversion's kind, truncation, or a change of sign alone; a checked one throws `System.OverflowException` when the value does not fit, its input read with the conversion's input sign; to or from `float` or `double` `PureCatalogue`'s `conv.<from>.<to>`, the integer's type that of its stack slot and input sign, a change to the same precision being the value itself; to or from a native integer opaque, but for an array's length | IlLowererTests.ConversionsToEveryIntegralType, .FloatingPointAndDecimalAreTheOperationLoweringsFunctions, .RefusedInstructionsLowerTheirOperands; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014, P1-015 |
| Call | lowered: `IrCall` of the callee's `CallIdentityFactory` identity, the receiver as it is (a struct's read from its address; one of an interface's method converted to it through its `cast` map, as C#'s conversion is), then the arguments as their parameters' types, a `ref` one read after all of them, an `out` one passing nothing, and the `threw` edge; each `ref` or `out` argument's variable is written from the call's output (M4-003); `typeof(T)` is the shared `typeof.<T>` input; an operator of `System.Decimal` is its `PureCatalogue` function, applied as the IOperation lowering applies it, and a user-defined operator or conversion its `op:` pure function (M4-002); an auto-property's accessor reads or writes its backing field's map at the receiver, a struct's through the struct's place (M4-008); opaque, its operands lowered first, when the callee does not resolve, returns by reference, is `constrained.` to a type parameter, or writes other than a variable, or one variable twice, through `ref` or `out` arguments; a call to an identity the pair's IOperation lowering found rebound (ADR 0042) is the same opaque, reason `rebound-call`, with no fingerprint and no threw edge, and one more per `ref` or `out` argument | IlLowererTests.CallIdentitiesMatchTheOperationLowering, .OperatorsAndAutoPropertiesAreLoweredAsTheOperationLoweringLowersThem, .AnAddressIsItsPlace, .DecimalOperatorIsItsCataloguedFunction, .AnInterfacesReceiverIsOfTheInterface; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp; ReboundCallLoweringTests.TheIlLoweringMakesAReboundCallTheSameOpaques | P1-014, P1-015, P2-069 |
| CallVirt | lowered: as `Call`, naming the override the receiver's static type binds (IL names the method it overrides; C# and the IOperation lowering name the override), with a receiver of a reference type null-checked after the arguments, `System.NullReferenceException` when it is null (its shadow, else `null.<Sort>`); a getter in the effect-free catalogue (ADR 0043: `System.String::get_Length()`) is the `IrPure` `get:<identity>` of its null-checked receiver, and no call (P2-071) | IlLowererTests.ACallvirtChecksItsReceiver, .ACallNamesTheOverrideItBinds, .AnInterfacesReceiverIsOfTheInterface; EffectFreeMembersTests.AStringsLengthIsAPureFunctionOfTheStringAndNoCall | P1-014, P1-015, P2-071 |
| NewObj | lowered: `IrCall` of the constructor, yielding the new object, never null; a constructor in the effect-free catalogue (ADR 0043) is no call: the object is the next of `new.<Sort>`, as the IOperation lowering makes it, without the `List<T>`/`Collection<T>` family rule, since a `newobj` carries no conversion (P2-071) | IlLowererTests.CallIdentitiesMatchTheOperationLowering; EffectFreeMembersTests.ACataloguedConstructorReadsTheNextNewObjectAndIsNoCall, .TheIlLoweringKeepsANewCollectionItsOwnSort | P1-014, P2-071 |
| NewObj[closure class] | opaque: a lambda's or local function's display class, whose name is an ordinal | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| Call[local function] | opaque: a local function's body is elsewhere and its name an ordinal | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| CallVirt[local function] | opaque: as `Call[local function]` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| Call[compiler-generated method] | opaque: a lambda's or another generated method's body is elsewhere | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| CallVirt[compiler-generated method] | opaque: as `Call[compiler-generated method]` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| TryCatch | lowered: its `try` block in place, then each handler's body from its first block, both going on to what follows; an exception raised in the block, walking out of the regions around the statement that raises it, goes to the first handler whose type it converts to implicitly, behind each `finally` it leaves, through `ExceptionLowerer`'s route (M4-008); one of no known type that more than one handler could take is `call-throw-in-try`; opaque, as a whole, when a handler's type does not resolve | IlLowererTests.AFilterDeclinesToTheNextHandler, .ACaughtExceptionReadIsOpaque, .AnUnloadedReferenceIsRefused; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| TryCatchHandler | lowered: a clause of its `TryCatch`; a `when` filter is copied onto each place an exception it may take is raised, its container's leave the branch to the handler or on to the next clause, and every exception in it declines, as `ExceptionLowerer.Filter` copies one | IlLowererTests.AFilterDeclinesToTheNextHandler | P1-015 |
| TryFinally | lowered: its `try` block in place, and its `finally` copied, through `ExceptionLowerer.Copy`, onto each path that leaves the block: its end, each `leave` or branch out of it (after a leave's value), and each exception's route, innermost first (M4-008) | IlLowererTests.FinallyIsCopiedOnEveryExit; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp; IlLoweringParityTests.IlAndOperationLoweringsOfOpaqueFreeSamplesAreEquivalent | P1-015 |
| TryFault | lowered: as `TryFinally`, its `fault` block copied onto each exception's route only, the path `TryFinally` shares; no C# a symbol names compiles to one (Roslyn emits `fault` only in an iterator's `MoveNext`) | IlLowererTests.FinallyIsCopiedOnEveryExit | P1-015 |
| Throw[new] | lowered: the constructor's call, with its own `threw` edge, then where an exception of the constructed type goes: `IrThrow` of it, or its handler; refused, its operands lowered first, when the constructor's call is | IlLowererTests.FinallyIsCopiedOnEveryExit, .ACallNamesTheOverrideItBinds; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| Throw[not new] | opaque: the type thrown is not known, as IOperation lowering leaves it | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| Rethrow | opaque: `throw;`, the exception in flight is not modelled | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdLoca[address operand] | lowered: the variable as a place, read and written through; a call's `ref` or `out` argument is one of its outputs, stored to the variable before its `threw` branch, an `in` one the value at the address (M4-003); refused for a caught exception or a variable the IR has no type for | IlLowererTests.AnAddressIsItsPlace, .AStructCopyDoesNotAliasItsSource | P1-015 |
| LdLoca[address escapes] | opaque: an address kept beyond one use | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdLoca[caught exception] | opaque: as `LdLoc[caught exception]` | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| LdFlda[address operand] | lowered: a field of an object is its `field.<Type>.<Field>` map at the receiver, upcast to the field's type, with the receiver's null check where the place is read or written or, when ILSpy does not mark it delayed, here (P2-017); a field of a struct is a place in the struct's place, read at the struct's value, and a write makes a new struct, the next of `new.<Sort>` with that field changed and the others copied, stored where the struct was; refused for a tuple element of an `IrTuple` sort, a `ref` field, a field that does not resolve, or a struct at no place (a struct's `this`) | IlLowererTests.AFieldWriteIsItsMapWrite, .AStructCopyDoesNotAliasItsSource, .AnAddressWithNoPlaceIsOpaque, .AnUnloadedReferenceIsRefused | P1-015 |
| LdFlda[address escapes] | opaque: as `LdLoca[address escapes]` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdsFlda[address operand] | lowered: the `field.<Type>.<Field>` map at the type's token; `decimal`'s `Zero`, `One`, `MinusOne`, `MaxValue` and `MinValue`, which Roslyn reads for those literals, are the constants | IlLowererTests.DecimalOperatorIsItsCataloguedFunction; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| LdsFlda[address escapes] | opaque: as `LdLoca[address escapes]` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdElema[address operand] | lowered: the array's slice of its sort's `array.<Sort>` map at an `int` index, of the array's own element type (ILSpy types the address by IL's opcode, `short` for a `char[]`'s `stelem.i2`), with the null check and then the unsigned bounds check against `length.<Sort>`, where the place is read or written or, not delayed, here (P1-006, P2-017); a slot holding one is that place; refused for several indices, a native index, or an element type that does not resolve | IlLowererTests.ArrayElementIsNullThenBoundsChecked, .AnAddressIsItsPlace, .AnAddressWithNoPlaceIsOpaque; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| LdElema[address escapes] | opaque: as `LdLoca[address escapes]` | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| AddressOf[address operand] | lowered: a fresh variable holding a copy of the value, as a place, so a field written through it never reaches the original | IlLowererTests.AStructCopyDoesNotAliasItsSource | P1-015 |
| AddressOf[address escapes] | opaque: as `LdLoca[address escapes]` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdObj | lowered: a read of the place its address names; opaque, its operands lowered first, when the address has no place | IlLowererTests.AFieldWriteIsItsMapWrite, .AnEscapingAddressIsOpaque; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| StObj | lowered: the place, then the value as the place's type, then the write, with a variable's null shadow and a slice's checks, yielding the value; opaque, its operands lowered first, when the address has no place | IlLowererTests.AFieldWriteIsItsMapWrite, .AnAddressIsItsPlace, .RefusedInstructionsLowerTheirOperands | P1-015 |
| LdcF4 | lowered: the `System.Single` element `TypeMapper.Constant` gives the value, M4-002's constant | IlLowererTests.FloatingPointAndDecimalAreTheOperationLoweringsFunctions | P1-015 |
| LdcF8 | lowered: as `LdcF4`, of `System.Double` | IlLowererTests.FloatingPointAndDecimalAreTheOperationLoweringsFunctions; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| LdcDecimal | lowered: as `LdcF4`, of `System.Decimal` | IlLowererTests.DecimalOperatorIsItsCataloguedFunction; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| DefaultValue | lowered: `default(T)` of a closed type with a constant default (`TypeMapper.Default`): `null`, `false`, zero; opaque for any other struct, as IOperation lowering leaves `default` of one | IlLowererTests.RefusedInstructionsLowerTheirOperands, .AnUnloadedReferenceIsRefused; IlLoweringParityTests.IlAndOperationLoweringsOfOpaqueFreeSamplesAreEquivalent | P1-015 |
| DefaultValue[type parameter] | opaque: `default(T)` of a type parameter, as IOperation lowering leaves it | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| NewArr | lowered: one `int` dimension (P2-001): a negative length throws `System.OverflowException` unless it is a constant, then `HeapLowerer.Allocate`'s fresh array of that length holding `default(T)`; its initialiser is its element stores; opaque for a native length, an element type that is an array, has no constant default or does not resolve | IlLowererTests.AnArrayIsCreatedAndMeasured; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| NewArr[rank > 1] | opaque: several dimensions, as IOperation lowering leaves it | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| LdLen | lowered: the array null-checked, then its sort's `length.<Sort>` map read at it, an `int` (a `Conv` of it from a native integer is of that `int`) | IlLowererTests.AnArrayIsCreatedAndMeasured; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| IsInst | lowered: `as` (M4-005): the operand's `istype.<From>.<To>` read, not null and passing, and then its `cast.<From>.<To>` or `null`, the failed test being its nullness; an operand already of the tested type passes when it is not null, with no `istype`; opaque, the operand lowered first, for an operand of a value type, a type parameter or a type no reference conversion relates, and for a tested value type or type parameter | IlLowererTests.DowncastThrowsInvalidCast, .ATypeTestReadsOnlyWhatItNeeds, .RefusedInstructionsLowerTheirOperands; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| CastClass | lowered: a downcast (M4-005): `System.InvalidCastException` when the operand is not null and fails its `istype`, and then `null` for a null operand or its `cast`; otherwise as `IsInst` | IlLowererTests.DowncastThrowsInvalidCast, .ATypeTestReadsOnlyWhatItNeeds, .AnUnloadedReferenceIsRefused; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| Box | lowered: the value's `cast.<T>.<To>` to the reference type it is used as, `object` or an interface (M3-010); opaque for a type parameter or a nullable | IlLowererTests.ABoxAndATypeAreTheirInputs; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-015 |
| Unbox | opaque: unboxing, as IOperation lowering leaves it | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| UnboxAny | opaque: unboxing, as IOperation lowering leaves it | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdTypeToken | lowered: as `Type.GetTypeFromHandle`'s argument, the call being the shared `typeof.<T>` input, never null (P2-002); opaque of a type parameter or a type that does not resolve, and anywhere else | IlLowererTests.ABoxAndATypeAreTheirInputs | P1-015 |
| LdFtn | lowered: a named method's pointer, as a delegate constructor's argument: the element of its sort that the method's call identity designates, so both sides that name the same method share it; refused for a method that does not resolve | IlLowererTests.AFunctionPointerNamesItsMethod, .AnUnloadedReferenceIsRefused | P1-015 |
| LdFtn[lambda] | opaque: a lambda, whose body is elsewhere and whose name is an ordinal | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| LdVirtFtn | lowered: as `LdFtn`, after the receiver's null check; a lambda is never virtual, so it has no `[lambda]` refinement | IlLowererTests.AFunctionPointerNamesItsMethod, .AnUnloadedReferenceIsRefused | P1-015 |
| BitNot | opaque: `~x`, not in P1-012's table | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| LdMemberToken | opaque: a member token (`ldtoken` of a field or method) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdObjIfRef | opaque: a read through a `ref` or a copy of a value | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdStrUtf8 | opaque: a UTF-8 string literal | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdVirtDelegate | opaque: a delegate bound to a virtual method | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdElemaInlineArray | opaque: an inline array's element address | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| GetPinnableReference | opaque: a `fixed` statement's pinnable reference | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| PinnedRegion | opaque: a `fixed` statement | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| CallIndirect | opaque: a call through a function pointer | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| Ckfinite | opaque: floating point | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| SizeOf | opaque: `sizeof(T)` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LocAlloc | opaque: `stackalloc` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LocAllocSpan | opaque: `stackalloc` into a span | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| Cpblk | opaque: a block copy | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| Initblk | opaque: a block initialisation | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| Arglist | opaque: `__arglist` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| MakeRefAny | opaque: `__makeref` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| RefAnyType | opaque: `__reftype` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| RefAnyValue | opaque: `__refvalue` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DebugBreak | opaque: a `break` opcode | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| InvalidBranch | opaque: IL ILSpy could not read | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| InvalidExpression | opaque: IL ILSpy could not read | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| NumericCompoundAssign | n/a: built by the statement transforms | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| UserDefinedCompoundAssign | n/a: built by the statement transforms | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicCompoundAssign | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| NullCoalescingInstruction | n/a: built by the statement transforms | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LockInstruction | n/a: built by the `lock` transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| UsingInstruction | n/a: built by the `using` transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| ThreeValuedBoolAnd | n/a: built by nullable lifting | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| ThreeValuedBoolOr | n/a: built by nullable lifting | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| NullableUnwrap | n/a: built by nullable lifting | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| NullableRewrap | n/a: built by nullable lifting | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| StringToInt | n/a: built by the string-switch transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| ExpressionTreeCast | n/a: built by the expression-tree transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| UserDefinedLogicOperator | n/a: built by the statement transforms | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicLogicOperatorInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicBinaryOperatorInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicUnaryOperatorInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicConvertInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicGetMemberInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicSetMemberInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicGetIndexInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicSetIndexInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicInvokeMemberInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicInvokeConstructorInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicInvokeInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DynamicIsEventInstruction | n/a: built by the dynamic call-site transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| MatchInstruction | n/a: built by the pattern-matching transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| YieldReturn | n/a: built by the iterator transform; an iterator's IL is a state machine, whose kickoff method is opaque for its `LdLoca` and `StObj` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| Await | n/a: built by the async transform; an async method's kickoff method is opaque for its `LdLoca` and `StObj` | IlLowererTests.UnmappedInstructionsAreOpaqueWithTheirKey | P1-012 |
| DeconstructInstruction | n/a: built by the deconstruction transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| DeconstructResultInstruction | n/a: built by the deconstruction transform | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| AnyNode | n/a: a pattern-matching placeholder, never in a function | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
