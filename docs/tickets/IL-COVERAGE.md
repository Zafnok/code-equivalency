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
| Branch | lowered: `IrGoto`, to a block of its own container or an enclosing one | IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| Leave | lowered: from the function's container `IrReturn` of its value as the return type; from a nested one `IrGoto` to what follows it | IlLowererTests.LoopsAndNestedContainersAgreeWithTheInterpreter; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| IfInstruction | lowered: `IrBranch` on its condition as Bool; with a value, as ILSpy inlines `&&` and `||` into an expression, each arm the value of the type wanted and a join | IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| SwitchInstruction | lowered: `IrSwitch` on an `int` or `long` value, one case per label of every section but the default, each label the value's own width's bits (ILSpy's labels are the sign-extended value, `uint` included); each section's body is lowered in a block of its own | IlLowererTests.ASwitchIsOneIrSwitch; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| SwitchSection | lowered: a case of its `IrSwitch`, or its default | IlLowererTests.ASwitchIsOneIrSwitch | P1-014 |
| LdLoc | lowered: the variable's SSA value; `this` of a class is the receiver input, as the IOperation lowering reads it where referenced; opaque when the variable's type has no sort (a `ref` parameter's or local's address, a struct's `this`, a pointer, a compiler-generated closure class), or when its value is used as a type no conversion takes it to (an enum as an integer) | IlLowererTests.RefusedInstructionsLowerTheirOperands; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| LdLoc[caught exception] | opaque: the caught exception object is not modelled, as `CaughtException` is not in IOperation lowering | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| StLoc | lowered: an SSA store of its value as the variable's type, and of the value's nullness to its shadow; opaque, its value lowered first, when the variable's type has no sort | IlLowererTests.RefusedInstructionsLowerTheirOperands; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| StLoc[ref local] | opaque: a `ref` local, as IOperation lowering leaves it | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| LdcI4 | lowered: `IrConst` of the type wanted: that width's bits, Bool (not zero), or an enum's element as `TypeMapper.Constant` spells its underlying value | IlLowererTests.ConstantsTakeTheTypeTheyAreUsedAs; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| LdcI8 | lowered: as `LdcI4` | IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| LdStr | lowered: the `System.String` element `TypeMapper.Constant` gives the text, the IOperation lowering's element for the same literal | IlLowererTests.ConstantsTakeTheTypeTheyAreUsedAs | P1-014 |
| LdNull | lowered: element 0 of the sort wanted | IlLowererTests.ConstantsTakeTheTypeTheyAreUsedAs | P1-014 |
| BinaryNumericInstruction | lowered: `IrBinary` on `int` or `long` operands, the instruction's `Sign`, not the operands' C# types, choosing `sdiv`/`udiv`, `srem`/`urem`, `ashr`/`lshr` and the overflow check; a zero divisor throws `System.DivideByZeroException` and, signed, `MinValue / -1` `System.OverflowException`, unchecked too; `add`, `sub` and `mul` with overflow checking throw `System.OverflowException`; a shift count is masked to the width, as `IrLowerer` shifts; `&`, `|` and `^` of two `bool` values are Bool; floating-point and native-integer operands are opaque (P1-015) | IlLowererTests.UnsignedDivisionFollowsTheInstructionSign; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| Comp | lowered: an `IrBinary` comparison of `int` or `long` operands by its `Sign`, or an equality of two values one of which is a `bool`, as Bool; a comparison of references, floating point or native integers is opaque (P1-015 reads the null shadow) | IlLowererTests.RefusedInstructionsLowerTheirOperands; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| LogicNot | n/a: ILSpy 11 has no such instruction; `!b` is a `Comp` of `b` against 0, lowered as `Comp` | IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| Conv | lowered: between integral types: extension by the conversion's kind, truncation, or a change of sign alone; a checked one throws `System.OverflowException` when the value does not fit, its input read with the conversion's input sign; to or from floating point, `decimal` or a native integer opaque (P1-015) | IlLowererTests.ConversionsToEveryIntegralType; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| Call | lowered: `IrCall` of the callee's `CallIdentityFactory` identity, the receiver as it is (not converted to the callee's type, as the IOperation lowering passes it), then the arguments as their parameters' types, and the `threw` edge; a user-defined operator or conversion is its `op:` pure function (M4-002); an auto-property's accessor reads or writes its backing field's map at the receiver (M4-008); opaque, its operands lowered first, when the callee does not resolve, takes a `ref`, `out` or `in` argument, returns by reference, or is a `constrained.` call (P1-015) | IlLowererTests.CallIdentitiesMatchTheOperationLowering, .OperatorsAndAutoPropertiesAreLoweredAsTheOperationLoweringLowersThem; IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp | P1-014 |
| CallVirt | lowered: as `Call`, with the receiver null-checked after the arguments, `System.NullReferenceException` when it is null (its shadow, else `null.<Sort>`) | IlLowererTests.ACallvirtChecksItsReceiver | P1-014 |
| NewObj | lowered: `IrCall` of the constructor, yielding the new object, never null | IlLowererTests.CallIdentitiesMatchTheOperationLowering | P1-014 |
| NewObj[closure class] | opaque: a lambda's or local function's display class, whose name is an ordinal | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| Call[local function] | opaque: a local function's body is elsewhere and its name an ordinal | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| CallVirt[local function] | opaque: as `Call[local function]` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| Call[compiler-generated method] | opaque: a lambda's or another generated method's body is elsewhere | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| CallVirt[compiler-generated method] | opaque: as `Call[compiler-generated method]` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| TryCatch | opaque: exception regions (P1-015, M4-008's lowering); one every path of which leaves ends its block with a return of the opaque's value | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| TryCatchHandler | opaque: inside its `TryCatch` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| TryFinally | opaque: as `TryCatch` (`using`, `lock`, `foreach` over an enumerator) | IlLowererTests.UnmappedInstructionsAreOpaqueWithTheirKey | P1-015 |
| TryFault | opaque: as `TryCatch` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| Throw[new] | opaque: `throw new T(...)`, which ends its block (P1-015 lowers it to `IrThrow`) | IlLowererTests.KeysAreRefinedByPosition | P1-015 |
| Throw[not new] | opaque: the type thrown is not known, as IOperation lowering leaves it | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| Rethrow | opaque: `throw;`, the exception in flight is not modelled | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdLoca[address operand] | opaque: a local's address as a receiver or `ref`/`out` argument (P1-015) | IlLowererTests.KeysAreRefinedByPosition | P1-015 |
| LdLoca[address escapes] | opaque: an address kept beyond one use | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdLoca[caught exception] | opaque: as `LdLoc[caught exception]` | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| LdFlda[address operand] | opaque: a field's address read or written through (P1-015: `field.<Type>.<Field>` map) | IlLowererTests.KeysAreRefinedByPosition | P1-015 |
| LdFlda[address escapes] | opaque: as `LdLoca[address escapes]` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdsFlda[address operand] | opaque: a static field's address (P1-015) | IlLowererTests.KeysAreRefinedByPosition | P1-015 |
| LdsFlda[address escapes] | opaque: as `LdLoca[address escapes]` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdElema[address operand] | opaque: an array element's address (P1-015: `array.<Sort>` map and bounds check) | IlLowererTests.KeysAreRefinedByPosition | P1-015 |
| LdElema[address escapes] | opaque: as `LdLoca[address escapes]` | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| AddressOf[address operand] | opaque: a temporary for a struct receiver (P1-015) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| AddressOf[address escapes] | opaque: as `LdLoca[address escapes]` | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdObj | opaque: a read through an address (P1-015) | IlAstReaderTests.LocalsAndSpansComeFromThePdb | P1-015 |
| StObj | opaque: a write through an address (P1-015) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| LdcF4 | opaque: a `float` constant (P1-015) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| LdcF8 | opaque: a `double` constant (P1-015) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| LdcDecimal | opaque: a `decimal` constant (P1-015) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| DefaultValue | opaque: `default(T)` of a closed type (P1-015) | IlLowererTests.KeysAreRefinedByPosition | P1-015 |
| DefaultValue[type parameter] | opaque: `default(T)` of a type parameter, as IOperation lowering leaves it | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| NewArr | opaque: a one-dimensional array creation (P1-015: P2-001's lowering) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| NewArr[rank > 1] | opaque: several dimensions, as IOperation lowering leaves it | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| LdLen | opaque: an array's length (P1-015: `length.<Sort>`) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| IsInst | opaque: a type test (P1-015: M4-005's `istype` and `cast`) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| CastClass | opaque: a downcast (P1-015) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| Box | opaque: a boxing conversion (P1-015: M3-010's `cast`) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| Unbox | opaque: unboxing, as IOperation lowering leaves it | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| UnboxAny | opaque: unboxing, as IOperation lowering leaves it | IlLowererTests.EveryKeyOfTheTableHasARow | P1-012 |
| LdTypeToken | opaque: `typeof(T)` (P1-015: the shared `typeof.<T>` input) | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
| LdFtn | opaque: a named method as a delegate's target (P1-015: a designated element naming it); shared when both sides name the same method | IlLowererTests.KeysAreRefinedByPosition | P1-015 |
| LdFtn[lambda] | opaque: a lambda, whose body is elsewhere and whose name is an ordinal | IlLowererTests.KeysAreRefinedByPosition | P1-012 |
| LdVirtFtn | opaque: as `LdFtn`, after the receiver's null check (P1-015); a lambda is never virtual, so it has no `[lambda]` refinement | IlLowererTests.EveryKeyOfTheTableHasARow | P1-015 |
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
