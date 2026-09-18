# M2-003 Lowering v1: Roslyn CFG to SSA IR for straight-line and branching code
Status: todo
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
   Type map: `sbyte/byte`=bv8, `short/ushort`=bv16, `int/uint/char`=bv32,
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
- [ ] `IrLowerer`, `TypeMapper`, `OperatorMapper`, `SsaBuilder`, `CallIdentityFactory`,
      each independently unit-tested with `AdhocWorkspace` compilations of small snippets.
- [ ] Snapshot tests (Verify): IR dump for at least 12 snippets covering each lowered
      construct, including nested `if`, `checked` arithmetic, division, an opaque call,
      and a method whose body is entirely opaque.
- [ ] Lowering oracle (property, CsCheck, 200 cases, seed printed on failure): generate a
      straight-line-plus-if method over `int`/`long`/`bool` params from a mini-AST; render
      to C#; compile all cases of a run into one in-memory assembly with
      `CSharpCompilation.Emit` and invoke by reflection (reflection is fine in tests);
      lower each and run `IrInterpreter`; compare return value and thrown exception type
      across 20 random inputs each. Generator lives in `Equiv.TestSupport`.
- [ ] `docs/tickets/IOPERATION-COVERAGE.md` rows for every `OperationKind` touched
      (lowered or opaque with reason).

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
