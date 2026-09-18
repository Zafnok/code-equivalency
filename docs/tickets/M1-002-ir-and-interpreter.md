# M1-002 IR types, validator, text format, interpreter, generators
Status: todo
Effort: L
Model: Opus, medium effort. Sonnet only at high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M0-005

## Goal
`Equiv.Core.Ir` exists and is the contract every later ticket builds on: the record
types from VERIFICATION-MODEL.md section 2, a validator, a text dump and parser, an
interpreter (production code, used as the test oracle and later to replay
counterexamples), and CsCheck generators in a shared test-support project.

## Spec references
VERIFICATION-MODEL.md sections 2 and 7; ARCHITECTURE.md (Core has no Roslyn/Z3);
CLAUDE.md naming rules; the architecture tests already enforce `Ir*` in `Equiv.Core.Ir`.

## Design

Types (all `sealed record`, immutable collections):

- `IrType`: `IrBool`, `IrBitVec(int Width)` with Width in {8,16,32,64},
  `IrSort(string Name)`, `IrMap(IrType Key, IrType Value)` (SSA heap slices, see below).
- `IrVar(string Name, IrType Type, string? SourceName)`. `SourceName` is the source
  local or parameter name when known; M3-002 uses it to align loop variables.
- `IrProcedure(ProcedureIdentity Identity, ImmutableArray<IrVar> Parameters,
  IrType? ReturnType, ImmutableArray<IrBlock> Blocks, IrBlockId Entry)`.
  `ProcedureIdentity` is a placeholder record `(string Value)` here; M1-003 replaces it.
- `IrBlock(IrBlockId Id, ImmutableArray<IrInstruction> Instructions, IrTerminator Terminator)`.
- Instructions (operands are always `IrVar`; constants go through `IrConst`):
  `IrConst(IrVar Target, IrValue Value)`, `IrBinary(IrVar Target, IrBinaryOp Op, IrVar A, IrVar B)`,
  `IrOverflows(IrVar Target, IrBinaryOp Op, IrVar A, IrVar B)` (Bool: would the op
  overflow; signedness is part of the op), `IrUnary(IrVar Target, IrUnaryOp Op, IrVar A)`
  (Neg, Not, BoolNot, ZExt/SExt/Trunc carry the target width),
  `IrPhi(IrVar Target, ImmutableArray<(IrBlockId From, IrVar Value)>)`,
  `IrCall(IrVar? Target, IrVar? Threw, CallIdentity Callee, ImmutableArray<IrVar> Args)`,
  `IrMapRead(IrVar Target, IrVar Map, IrVar Key)`, `IrMapWrite(IrVar Target, IrVar Map, IrVar Key, IrVar Value)`,
  `IrOpaque(IrVar? Target, string Reason, SourceSpan Span)`.
  Fields and arrays both use the map instructions: a field `f` is one map var per SSA
  version keyed by object ref; an array is a map keyed by index. The frontend chooses.
- `IrBinaryOp`: Add, Sub, Mul, SDiv, SRem, UDiv, URem, And, Or, Xor, Shl, AShr, LShr,
  Eq, Ne, Slt, Sle, Sgt, Sge, Ult, Ule, Ugt, Uge. Bool operands allowed only for
  And, Or, Xor, Eq, Ne. Sort operands allowed only for Eq, Ne.
- Terminators: `IrGoto(IrBlockId)`, `IrBranch(IrVar Cond, IrBlockId Then, IrBlockId Else)`,
  `IrSwitch(IrVar Scrutinee, ImmutableArray<(IrValue, IrBlockId)>, IrBlockId Default)`,
  `IrReturn(IrVar? Value)`, `IrThrow(string ExceptionType)`, `IrUnreachable` (assume
  false; used by loop unrolling in M3-002).

IR instructions never throw. Every exception edge is explicit: the frontend lowers
`checked(a + b)` to `IrOverflows` + `IrBranch` to an `IrThrow` block + `IrBinary`; a
call that may throw gets a `Threw` Bool and a branch. This keeps the interpreter and
the Z3 encoder trivial and identical in meaning.

Validator (`IrValidator.Validate(IrProcedure) -> ImmutableArray<IrDiagnostic>`), one
rule per diagnostic id: unique block ids; entry exists; every target var assigned
exactly once (parameters count as assigned); every use dominated by its definition
(compute dominators, Cooper-Harvey-Kennedy); phi predecessors are exactly the block's
predecessors; phis only at the start of a block; operand types match the op table
above; branch targets exist; `IrMap` types line up on read/write.

Text format (`IrText.Dump` / `IrText.Parse`), pinned by snapshot tests, roughly:

```
proc "Ns.Type::M(int32,int32)" (a: bv32, b: bv32) -> bv32 entry B0
B0:
  %t0 = const bv32 1
  %t1 = add %a, %t0
  %c  = slt %t1, %b
  br %c, B1, B2
B1:
  ret %t1
B2:
  throw "System.InvalidOperationException"
```

Parser errors carry line/column. The parser is what makes hand-written IR fixtures in
later test projects readable; treat it as a first-class deliverable.

Interpreter (`IrInterpreter.Run(IrProcedure, IrInputs, ICallOracle, stepBudget) -> IrRun`):
values are `IrValue` (Bool, BitVec with width and unsigned magnitude, Sort as an
opaque token with an id, Map as an immutable dictionary with a default). BitVec ops
wrap; `IrOverflows` implements the signed/unsigned overflow predicates. `ICallOracle`
answers calls deterministically from `(CallIdentity, args)` and records the call trace.
`IrRun` records the outcome (Returned value / Threw type / Budget exhausted / Opaque
reached), final out-params, and the trace. The interpreter lives in `Equiv.Core`
because M3-001 replays solver models through it.

Generators live in a new non-test library `tests/Equiv.TestSupport` (the one permitted
shared test project; see the task-loop skill). Generate from a small structured AST
(sequence, if/else, bounded loop with a counter) and lower it to IR with a tiny SSA
builder; do not try to generate arbitrary CFGs and then repair SSA. Provide:
`IrGen.Procedure` (well-formed), `IrGen.Mutation(IrProcedure)` (semantics-changing edits:
swap operands of a non-commutative op, flip a branch, change a constant), and a
`Violations` generator producing one invalid procedure per validator rule.

## Deliverables
- [ ] Types above in `src/Equiv.Core/Ir/`, one file per record family.
- [ ] `IrValidator` with a diagnostic id per rule; unit test per rule (valid and invalid).
- [ ] `IrText` dump and parse; snapshot tests for three hand-written procedures; property
      test: `Parse(Dump(p)) == p` for generated `p`.
- [ ] `IrInterpreter` and `ICallOracle`; unit tests per instruction and terminator kind,
      including wrap-around and overflow predicates at width boundaries.
- [ ] `tests/Equiv.TestSupport` with `IrGen`; property test: every generated procedure
      validates clean; every `Violations` case fails with the expected id; every
      `Mutation` produces a different interpreter result for some generated input
      (or is discarded by the generator; document the discard rate).
- [ ] Row added to `docs/adr/0002-dependencies.md` only if a package is added (none expected).

## Pitfalls
- Do not model `Sort` equality as reference equality in the interpreter; tokens compare by id.
- Signed vs unsigned is on the op, not the type. `IrBitVec` has no sign.
- Keep `IrValue` free of `System.Numerics.BigInteger` in the public API; use `ulong` bits
  plus width.
- Determinism: dump ordering follows block order in the procedure, not a hash set.

## Out of scope
Any Roslyn or Z3 code. Procedure identity normalisation (M1-003).

## Notes
