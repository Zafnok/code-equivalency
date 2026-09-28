# P1-012 IL-lowering spike: gitextensions-8522 (2026-09-28)

Question: of the changed pairs still opaque after the kept M4 tickets, how many would a fallback
that lowers from ILSpy's ILAst reach with no unmapped instruction? And does IL break congruence,
because the two sides compile to IL of different shapes?

**Answer: 114 of 1,143 changed pairs (10.0%) become lowerable. Compiler shape drift touches 19
(1.7%), under half the gain (5.0%).** Both of criterion 4's conditions hold. ADR 0039 (proposed)
therefore records the IL fallback, with IOperation staying primary. Every one of the 19 drift pairs
is an API binding change (another overload, a `ref` return, a different `Deconstruct`), not a
codegen difference. Six of them are changes that ADR 0024's serialisation hides from IOperation.

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`).
- equiv: aca3f83 (`main`), win-x64, loaded through the production frontend, default config.
- Tool: `tools/spikes/il-lowering/` (about 790 lines of C#), with `ICSharpCode.Decompiler` 11.1.0.9782
  (MIT; ADR 0002 row, spike only). Wall-clock 140 s: 122 s to load and lower, 17 s to emit and
  decompile.
- Command: see `tools/spikes/il-lowering/README.md`. The run was
  `dotnet <spike> <legacySolution> <modernSolution> <equiv.sarif>`, where `equiv.sarif` is the
  latest full run of the pair (the P2-033 branch, 2026-09-27, 1,197 changed pairs at that commit).
  The SARIF only adds the Unknown(opaque) subset below.

## Method
**Population.** The census's changed pairs at aca3f83 (not congruent under ADR 0024), minus two
groups: pairs with no opaque, and pairs whose every opaque is a fragment the other side shares
(M4-004 already turns those into one shared call). What remains holds at least one *unshared*
opaque, which is what "still opaque after the kept M4 tickets" means. The denominator is every
changed pair, as in ADR 0034.

**ILAst.** Each project's `Compilation` is emitted in memory with its own options, as M4-009's
replay emits it, and read back with ILSpy's `ILReader`. The transform pipeline is ILSpy's own
without the transforms that rebuild C# constructs:

`ControlFlowSimplification → SplitVariables → ILInlining → InlineReturnTransform →
RemoveInfeasiblePathTransform → DetectPinnedRegions → DetectCatchWhenConditionBlocks →
DetectExitPoints → LdLocaDupInitObjTransform → EarlyExpressionTransforms → SplitVariables →
RemoveDeadVariableInit → ControlFlowSimplification → SwitchDetection → SplitVariables →
BlockILTransform[LoopDetection] → DetectExitPoints → BlockILTransform[ConditionDetection] →
CopyPropagation → RemoveRedundantReturn`

The pipeline leaves out async, iterators, `using`, `lock`, lambdas, local functions, display
classes, string and nullable switches, patterns, and the statement transforms. Each of those would
bring back the construct the fallback exists to avoid. Every method was found: no pair lost its
ILAst to a failed emit or lookup.

**Buckets.** A pair with an `async` or iterator method on either side is its own bucket. Its IL is
a state machine, and its kickoff method's IL says nothing about its body. Every other pair is
**lowerable** when every instruction on both sides has a key in the table below. Otherwise it is
not lowerable.

**Drift.** Drift is measured on the pairs whose C# declaration is token-identical, trivia
ignored. A constructor compares its whole type declaration, since its IL holds the field
initialisers. Such a pair shows
- *text drift* when the two ILAst texts differ once IL offsets, labels and variable names are
  renumbered by first appearance (an SSA lowering erases all three);
- *shape drift* when the opcode trees differ, which criterion 4 uses;
- *IL-only drift* when the ILAst differs although the two ADR 0024 serialisations are equal.
  IL-only drift is a difference that IOperation lowering does not see and IL lowering would.

## Mapping table

Each key is an ILAst opcode, refined by context where one opcode is lowerable in one position and
not in another. The table follows `IOPERATION-COVERAGE.md`'s own refusals wherever IOperation
declines a construct for a semantic reason, not for its syntax. So the following stay unmapped:
unboxing, reading a caught exception, a `ref` local, `throw` of anything but a `new`, `default(T)`
of a type parameter, and lambdas and local functions (their bodies are elsewhere, and their names
are ordinals). Anything not listed is unmapped.

| Key | IR it lowers to |
|---|---|
| `ILFunction`, `BlockContainer`, `Block`, `Nop` | the procedure and its `IrBlock`s |
| `Branch`, `Leave` | `IrGoto`, or `IrReturn` from the function's container |
| `IfInstruction` | `IrBranch` |
| `SwitchInstruction`, `SwitchSection` | `IrSwitch` and its cases |
| `TryCatch`, `TryCatchHandler`, `TryFinally`, `TryFault` | the `Try` lowering's exception edges and copied `finally` blocks (M4-008) |
| `Throw[new]` | `IrThrow` of the constructed type |
| `LdLoc`, `StLoc` | SSA variables |
| `LdLoca[address operand]` | a receiver or a `ref`/`out` argument (`IrOut`, M4-003) |
| `LdcI4`, `LdcI8` | `IrConst` bitvector or Bool |
| `LdcF4`, `LdcF8`, `LdcDecimal`, `LdStr`, `LdNull`, `DefaultValue` | `IrConst` designated element of the sort (`null` is element 0) |
| `BinaryNumericInstruction`, `Comp`, `LogicNot`, `Conv` | `IrBinary`, `IrOverflows`, `IrUnary`, or `IrPure` `f32.*`/`f64.*`/`dec.*`/`conv.*` (M4-002) |
| `Call`, `CallVirt`, `NewObj` | `IrCall` with a threw edge (and the receiver's null check) |
| `LdObj`, `StObj` | a read or write of the place its address names |
| `LdFlda[address operand]`, `LdsFlda[address operand]` | `field.<Type>.<Field>` map |
| `LdElema[address operand]`, `LdLen`, `NewArr` | `array.<Sort>` and `length.<Sort>` maps with the bounds check; `ArrayCreation` (P1-006, P2-001) |
| `AddressOf[address operand]` | a temporary for a struct receiver |
| `IsInst`, `CastClass`, `Box` | the type test and `cast.<From>.<To>` read, the downcast's `InvalidCastException` branch (M3-010, M4-005) |
| `LdTypeToken` | the shared `typeof.<T>` input |
| `LdFtn`, `LdVirtFtn` (a named method) | `IrConst` designated element naming the method, as a delegate constructor's argument |

An address is mapped only as an *address operand*: read or written through, the target of a field
or element address, passed to a call, or kept once for a read-modify-write.

## Counts

| | pairs | share of changed pairs |
|---|---|---|
| Matched pairs | 13,541 | |
| Changed pairs | 1,143 | 100% |
| ... with no opaque | 447 | 39.1% |
| ... whose only opaques are shared (M4-004) | 378 | 33.1% |
| ... **holding an unshared opaque (the population)** | 318 | 27.8% |
| ...... async or iterator state machine | 25 | 2.2% |
| ...... **lowerable: only mapped kinds, both sides** | **114** | **10.0%** |
| ...... not lowerable: some unmapped kind | 179 | 15.7% |
| ...... token-identical C# | 237 | 20.7% |
| ......... text drift | 64 | 5.6% |
| ......... **shape drift** | **19** | **1.7%** |
| ............ of which lowerable | 5 | 0.4% |
| ...... token-identical C# and equal serialisations | 194 | 17.0% |
| ......... IL-only text drift | 39 | 3.4% |
| ......... IL-only shape drift | 6 | 0.5% |

**The Unknown(opaque) subset.** 182 of the population's pairs were Unknown(opaque) in the full
run: 60 lowerable (5.2% of changed pairs), 104 not, and 18 state machines. Shape drift among the
182 is 9 (0.8%).

**Which opaque reasons the gain removes.** Counts are pairs holding each unshared reason. The first
column is the lowerable pairs (114), the second the whole population (318).

| Reason | lowerable | population |
|---|---|---|
| `Binary` (lifted operators) | 37 | 46 |
| `Conversion` | 17 | 34 |
| `CompoundAssignment` | 16 | 17 |
| `switch-pattern` | 15 | 23 |
| `InterpolatedString` | 13 | 16 |
| `DeconstructionAssignment` | 8 | 25 |
| `undefined` | 6 | 10 |
| `ArrayCreation` | 5 | 5 |
| `DelegateCreation` | 3 | 118 |
| `InstanceReference` | 3 | 4 |
| `call-throw-in-try` | 3 | 6 |

## Ten most frequent unmapped kinds

Pairs holding each, among the 179 that are not lowerable. The third column counts pairs for which
that kind is the only unmapped one.

| Kind | pairs | sole unmapped kind |
|---|---|---|
| `LdFtn[lambda]` | 132 | 48 |
| `NewObj[closure class]` | 66 | 0 |
| `LdLoc[caught exception]` | 41 | 24 |
| `Call[local function]` | 28 | 13 |
| `LdMemberToken` | 5 | 1 |
| `UnboxAny` | 5 | 4 |
| `Rethrow` | 3 | 1 |
| `Throw[not new]` | 3 | 0 |
| `CallVirt[local function]` | 1 | 0 |
| `LdElema[address escapes]` | 1 | 0 |

`StLoc[ref local]` ties with the last two at 1 pair. Lambdas and their closures dominate: IL does
not help with a delegate whose body is another method.

## Drift, read
The shape drift has three causes on this pair, and none of them is codegen:
- the modern BCL has a new overload, so a call that took a `params` array (`NewArr` plus element
  stores) becomes a call with plain arguments, or gains optional-argument constants;
- `foreach` deconstruction of a key-value pair binds to the repository's own `Deconstruct`
  extension on .NET Framework and to the BCL's instance method on .NET 5;
- on the modern side, a test's argument matcher from the mocking package (an `Any()` call)
  returns `ref T`, so its result is read through `LdObj`.

IOperation lowering sees the first cause as a different callee as well. The other two are the six
IL-only shape-drift pairs: ADR 0024's serialisation hides a changed deconstruction method and a ref
return, and IL does not. The 39 IL-only text-drift pairs differ only in closure-class and
local-function ordinals (`<>c__DisplayClass120_0` against `<>c__DisplayClass117_0`). Those pairs
also hold lambdas, so none of them is lowerable. Across all 12,977 matched pairs with token-identical
C#, IL-only shape drift is 101 (0.8%). Both sides are compiled by one Roslyn, so a compiler version
cannot cause drift here. What remains is the language version and the target BCL. C# 10's
interpolated-string handler is the one construct known to split, and it appears only on a modern
side that targets .NET 6 or later: `samples/business-layer` shows it, Git Extensions (.NET 5) does
not.

## Reading
An IL fallback reaches 10.0% of the changed pairs, about a third of the tail still opaque after M4.
The per-construct route would need a ticket for each reason those pairs hold: at least lifted
operators, conversions, compound assignment, type switches, interpolation and deconstruction. The
drift it brings is small, and it is binding drift that IOperation mostly sees as well. Lowerable is
not proved, though. The IL routes a lifted operator through `Nullable<T>` getters, and
interpolation through `string.Format` or `Concat`. Those are uninterpreted calls unless a catalogue
entry covers them. So the gain in Equivalent verdicts will be smaller than 10.0%, and this spike
does not measure it. Lambdas remain the largest single blocker, and IL does not change that.
