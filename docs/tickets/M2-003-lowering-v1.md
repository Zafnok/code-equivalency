# M2-003 Lowering v1: Roslyn CFG to SSA IR for straight-line and branching code
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M2-002, M1-002

## Goal
`IrLowerer.Lower(IMethodBodyOperation, SemanticModel) -> IrProcedure` for: parameters and
locals of integral and bool types, integer arithmetic (checked and unchecked), bool
logic, comparisons, `if`/`else`, `return`, and calls to anything (opaque). Every other
`OperationKind` produces `IrOpaque`. The lowering-oracle property test proves the
lowering agrees with the C# compiler on generated methods.

## Spec references
VERIFICATION-MODEL.md sections 2, 3, 7 (lowering oracle); ADR 0003; the `equiv-extend-ir`
skill (follow it for each construct).

## Design

Input: `ControlFlowGraph.Create(IMethodBodyOperation)` where the operation comes from
`semanticModel.GetOperation(methodDeclarationSyntax)`. Work only from the CFG, never from
syntax. What the Roslyn CFG gives you:

- `cfg.Blocks`: `BasicBlockKind.Entry`, `Block`, `Exit`; `Operations` (statements, already
  flattened: `ISimpleAssignmentOperation`, `IExpressionStatementOperation`,
  `IFlowCaptureOperation`); `BranchValue` with `ConditionKind` (`WhenTrue`/`WhenFalse`);
  `FallThroughSuccessor` and `ConditionalSuccessor` (each a `ControlFlowBranch` with
  `Destination` and `Semantics`: Regular, Return, Throw, Rethrow, StructuredExceptionHandling).
- Temporaries are `IFlowCaptureOperation` (define) and `IFlowCaptureReferenceOperation`
  (use), keyed by `CaptureId`. Treat each capture id as a local.
- Locals: `ILocalReferenceOperation` (`Local` symbol); parameters:
  `IParameterReferenceOperation`. Literals: `ILiteralOperation.ConstantValue`.
- Exception regions: `cfg.Root.NestedRegions` with `ControlFlowRegionKind`. v1: if the
  method has any `Try`/`Catch`/`Finally`/`Filter` region, the whole procedure body is one
  `IrOpaque` with reason `try-region` (M2-004 handles them).
- Unreachable blocks: `IsReachable == false`; drop them.

Two passes:

1. **Block lowering** (no SSA yet): map each CFG block to an `IrBlock` with variables
   named after locals/captures (`local:x`, `capture:3`, `param:a`) and multiple
   assignments allowed. Expressions are lowered recursively into temporaries.
   Type map (ADR 0013): `sbyte/byte`=bv8, `short/ushort/char`=bv16, `int/uint`=bv32,
   `long/ulong`=bv64, `bool`=Bool, everything else `IrSort(fullyQualifiedMetadataName)`.
   `IConversionOperation` between integral types: `ZExt`/`SExt`/`Trunc` chosen from source
   signedness and widths; between anything else: `IrOpaque`. `IBinaryOperation`: op table
   from `BinaryOperatorKind`; signedness from the operand type; `IsChecked` or the
   containing `checked` context (`IsChecked` is on the operation) produces
   `IrOverflows` + branch to a shared `IrThrow("System.OverflowException")` block.
   Division: emit a zero test + `IrThrow("System.DivideByZeroException")` before `SDiv`/`UDiv`.
   `IInvocationOperation`: `IrCall` with `Threw` var + branch to `IrThrow("System.Exception")`;
   `CallIdentity` from the method symbol via `SymbolDisplayFormat.FullyQualifiedFormat`
   with parameter types (share the helper with M2-002). Receiver is the first arg.
   Anything not listed: `IrOpaque(target, reason: operation.Kind.ToString(), span)`.
2. **SSA construction**: use Braun, Buchwald, Hack, Leißa, Mallon, Zwinkau, "Simple and
   Efficient Construction of Static Single Assignment Form" (CC 2013): on-the-fly
   renaming with `readVariable`/`writeVariable` per block, incomplete phis sealed when
   all predecessors are known, trivial-phi removal. It needs no dominator tree, works on
   any reducible graph, and is about 150 lines. The Roslyn CFG is always reducible.
   Run `IrValidator` on the result in debug builds and in every test.

## Deliverables
- [x] `IrLowerer`, `TypeMapper`, `OperatorMapper`, `SsaBuilder`, `CallIdentityFactory`,
      each independently unit-tested with `AdhocWorkspace` compilations of small snippets.
- [x] Snapshot tests (Verify): IR dump for at least 12 snippets covering each lowered
      construct, including nested `if`, `checked` arithmetic, division, an opaque call,
      and a method whose body is entirely opaque.
- [x] Lowering oracle (property, CsCheck, 200 cases, seed printed on failure): generate a
      straight-line-plus-if method over `int`/`long`/`bool` params from a mini-AST; render
      to C#; compile all cases of a run into one in-memory assembly with
      `CSharpCompilation.Emit` and invoke by reflection (reflection is fine in tests);
      lower each and run `IrInterpreter`; compare return value and thrown exception type
      across 20 random inputs each. Generator lives in `Equiv.TestSupport`.
- [x] `docs/tickets/IOPERATION-COVERAGE.md` rows for every `OperationKind` touched
      (lowered or opaque with reason).

## Acceptance criteria (all must hold; nothing beyond them)
1. `ProcedurePair` gains `IrProcedure? OldBody` and `IrProcedure? NewBody`, filled by
   `CSharpFrontend.Analyze` for every matched pair; `IrValidator` reports zero
   diagnostics on every lowered body from the samples (test).
2. The 12 snapshot snippets listed under Tests exist and are verified.
3. The lowering oracle passes 200 cases with a fixed seed and the seed is printed on
   failure.
4. Every `OperationKind` encountered in the samples appears in
   `IOPERATION-COVERAGE.md` as `lowered` or `opaque` with a reason; none is missing.
5. A method containing a loop, `switch`, `try`, field, array or reference-typed
   dereference lowers with `IrOpaque` nodes (not an exception) and the reason names the
   construct; those are M2-004's job.

## Size guard
Five source files in `src/Equiv.Frontend.CSharp/Lowering/`. If the SSA builder exceeds
about 250 lines, you are not following Braun et al.; re-read section 2 of the paper.

## Pitfalls
- `IFlowCaptureOperation` can capture a value used across blocks; it is a variable, not
  an expression. Missing this produces uses without definitions.
- Roslyn evaluates `&&`/`||` into explicit branches in the CFG; you get no
  `ConditionalAnd` binary op there. Do not add short-circuit handling of your own.
- `char` arithmetic promotes to `int` in C#; the CFG already has the conversion, but
  check the operand types when choosing widths.
- Constant folding: Roslyn keeps `ConstantValue` on operations; do not fold yourself.
  The Z3 backend does not care, and folding hides bugs from the oracle.
- The SSA builder must create the shared throw blocks before sealing; a throw block
  has no phis.

## Out of scope
Loops, `switch`, `try`, null handling, fields, arrays, strings (M2-004). Any Z3.

## Notes
- Decision: `char` width -> bv16, unsigned (ADR 0013, accepted). Alternatives: bv32 as first written, Sort. Rule: ADR.
- Decision: lowerer entry point -> `IrLowerer.Lower(IMethodBodyOperation, SemanticModel, RenameMap)`; the frontend calls `IrLowerer.Lower(IMethodSymbol, Compilation, RenameMap)`, which lowers a constructor, an arrow-bodied property or an auto-accessor as one whole-body `IrOpaque` whose reason is the operation kind (or `no-body`). Alternatives: the two-argument signature (then call identities and the procedure identity cannot see the config's rename map), a lowerer instance. Rule: 1.
- Decision: pass-1 variables -> keyed by the Roslyn symbol or `CaptureId`, not by `local:x` strings; SSA names are `<source>` for parameters, `<source>.<n>` for later versions, `$<n>` for temporaries and `$c<id>.<n>` for captures. Alternatives: `local:x` names (the IR text format does not allow `:` in a name). Rule: 3.
- Decision: loops, `switch` and exception regions -> the whole body becomes one `IrOpaque` with reason `loop`, `switch` or `try-region`. Loops and switches are found as `ILoopOperation`/`ISwitchOperation`/`ISwitchExpressionOperation` in the operation tree, because the CFG has already turned them into plain branches. A `goto` loop has no loop operation, so it is lowered as ordinary blocks; the SSA builder handles back edges. Alternatives: opaque only at the construct. Rule: 4.
- Decision: `throw` -> `IrOpaque` with reason `Throw` plus an opaque exit. The thrown object's dynamic type is not known statically, and building it is an `ObjectCreation`, which is opaque anyway. Alternatives: `IrThrow(static type)`. Rule: 4.
- Decision: signed `/` and `%` -> zero test (`DivideByZeroException`), then `IrOverflows sdiv` (`OverflowException`) in every context, because .NET throws on `MinValue / -1` and `MinValue % -1` even when unchecked. The lowering oracle pins this. Alternatives: overflow test only when checked. Rule: 3.
- Decision: shift counts -> masked with `width - 1` (the C# rule), then zero-extended to the left operand's width. Alternatives: raw SMT shift. Rule: 1.
- Decision: checked integral conversion -> throws when the round trip back to the source type differs, or when exactly one side is signed and the signed-side value is negative. Alternatives: range constants per type pair. Rule: 4.
- Decision: an operation whose Roslyn `ConstantValue` is integral or bool -> `IrConst` of that value. This is Roslyn's fold, not ours, and it covers `const` locals and fields. Alternatives: lower only `ILiteralOperation`. Rule: 1.
- Decision: call identity -> the M2-002 `RoslynIdentity` string plus `<typeArgs>` for a constructed generic method or type, so `F<int>()` and `F<long>()` are different functions. Type arguments are not renamed; that is the same limit as the normaliser's nested-generic rule. Alternatives: the definition only (unsound for generic calls). Rule: 4.
- Decision: instance calls on a receiver that is not a value type -> `IrOpaque` with reason `dereference` (null handling is M2-004). A `ref`/`out` argument -> reason `ref-argument`. Compound assignment and `++`/`--` are not listed in the ticket, so they are opaque by operation kind. Alternatives: `IrCall` with a Sort receiver. Rule: 4.
- Decision: shared throw blocks -> one per exception type; the builder may give them phis for `ref`/`out` outs (the "throw block has no phis" pitfall holds only when there are no by-ref parameters). Alternatives: one throw block per site. Rule: 1.
- Decision: a read with no reaching definition (only possible in code with compile errors) -> an `IrOpaque` with reason `undefined` at the top of the entry block. A Regular fall-through into the exit of a non-void method (also only possible in erroneous code) -> reason `missing-return`. Alternatives: throw. Rule: 4 (frontend never throws on unsupported input).
- Decision: oracle generator -> `Equiv.TestSupport/LoweringOracleGen.cs`, which emits C# text only (TestSupport does not reference Roslyn); compilation, reflection and lowering live in `Equiv.Frontend.CSharp.Tests`. Alternatives: extend `IrGenAst` (it is IR-level and not C#-shaped). Rule: 4.
- Note: acceptance criterion 2 refers to "the 12 snapshot snippets listed under Tests", but this ticket has no Tests section. The 16 snippets in `IrLowererSnapshotTests` cover every construct the Deliverables list names.
- Note: `RoslynTestCompilations.Compile` (M2-002) never applied its metadata references, because `Project.WithMetadataReferences` returns a copy the `AdhocWorkspace` never sees. So every snippet compiled without corlib, and `int` was an error type. It now builds the project from a `ProjectInfo`. With corlib present, `ProcedureEnumeratorTests` no longer reached the excluded-`MethodKind` branch, so its fixture gained a destructor.
- Note: when an assignment's right-hand side branches (`x = c ? a : b` under `checked`, for example), the CFG captures the left-hand local first and assigns through an `IFlowCaptureReference`. The lowering oracle found this on its first run.
- Note: the oracle confirmed on .NET 10 x64 that `long.MinValue / -1` throws `OverflowException` in unchecked code; the model relies on this.
- Note: a constructor body's reason is `ConstructorBodyOperation`, the `OperationKind` enum name, not `ConstructorBody`.
- Note: CsCheck seeds are 12-character strings; `000000000000` is valid.
- Note: size guard. `SsaBuilder.cs` has 245 non-comment lines (320 physical). The Braun core (read/write/seal/trivial-phi) is about 90 of them; the rest is the draft-step plumbing and the final operand rewrite.
