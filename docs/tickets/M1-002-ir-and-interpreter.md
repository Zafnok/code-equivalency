# M1-002 IR types, validator, text format, interpreter, generators
Status: in-progress
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
- `IrParameter(IrVar Var, IrParameterKind Kind)` with `IrParameterKind { In, Ref, Out }`.
- `IrProcedure(ProcedureIdentity Identity, ImmutableArray<IrParameter> Parameters,
  IrType? ReturnType, ImmutableArray<IrBlock> Blocks, IrBlockId Entry)`.
  `ProcedureIdentity` is a placeholder record `(string Value)` here; M1-003 replaces it.
  A `Ref`/`Out` parameter is an ordinary SSA input whose later versions are ordinary
  SSA vars; its final value is whatever version the exit terminator names (below).
- `IrBlock(IrBlockId Id, ImmutableArray<IrInstruction> Instructions, IrTerminator Terminator)`.
- Instructions (operands are always `IrVar`; constants go through `IrConst`):
  `IrConst(IrVar Target, IrValue Value)`, `IrBinary(IrVar Target, IrBinaryOp Op, IrVar A, IrVar B)`,
  `IrOverflows(IrVar Target, IrOverflowOp Op, IrVar A, IrVar B)` (Bool: would the op
  overflow) with `IrOverflowOp { SAdd, UAdd, SSub, USub, SMul, UMul, SDiv }`; each maps
  one-to-one onto a Z3 predicate in M3-001 (signed add and sub need both the NoOverflow
  and NoUnderflow checks; `SDiv` is `a == MinValue && b == -1`, which C# throws on even
  unchecked; checked negation lowers as `SSub(0, a)`),
  `IrUnary(IrVar Target, IrUnaryOp Op, IrVar A)`
  (Neg, Not, BoolNot, ZExt/SExt/Trunc carry the target width),
  `IrPhi(IrVar Target, ImmutableArray<(IrBlockId From, IrVar Value)>)`,
  `IrCall(IrVar? Target, IrVar? Threw, CallIdentity Callee, ImmutableArray<IrVar> Args)`,
  `IrMapRead(IrVar Target, IrVar Map, IrVar Key)`, `IrMapWrite(IrVar Target, IrVar Map, IrVar Key, IrVar Value)`,
  `IrOpaque(IrVar? Target, string Reason, SourceSpan Span)`.
  Fields and arrays both use the map instructions: a field `f` is one map var per SSA
  version keyed by object ref; an array is a map keyed by index. The frontend chooses.
- `IrBinaryOp`: Add, Sub, Mul, SDiv, SRem, UDiv, URem, And, Or, Xor, Shl, AShr, LShr,
  Eq, Ne, Slt, Sle, Sgt, Sge, Ult, Ule, Ugt, Uge. Add, Sub, Mul have no signed variant
  because wrapping arithmetic is identical at the bit level; signedness only matters
  for division, remainder, shifts right, comparisons, and overflow tests. Bool operands
  allowed only for And, Or, Xor, Eq, Ne. Sort operands allowed only for Eq, Ne.
- Terminators: `IrGoto(IrBlockId)`, `IrBranch(IrVar Cond, IrBlockId Then, IrBlockId Else)`,
  `IrSwitch(IrVar Scrutinee, ImmutableArray<(IrValue, IrBlockId)>, IrBlockId Default)`,
  `IrReturn(IrVar? Value, ImmutableArray<IrOut> Outs)`,
  `IrThrow(string ExceptionType, ImmutableArray<IrOut> Outs)`, `IrUnreachable` (assume
  false; used by loop unrolling in M3-002). `IrOut(IrVar Param, IrVar Final)` names the
  SSA version of a `Ref`/`Out` parameter that is live at that exit. Both exit kinds carry
  `Outs` because a `ref` write before a throw is visible to the caller in C#. `Outs` lists
  every by-ref parameter in declaration order, once each, and is empty when there are
  none; the validator enforces this. The interpreter's `IrRun` reports the `Final` values
  from whichever exit fired, and M3-001 builds its per-parameter ite chain from them.

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
- [x] Types above in `src/Equiv.Core/Ir/`, one file per record family.
- [x] `IrValidator` with a diagnostic id per rule; unit test per rule (valid and invalid).
- [x] `IrText` dump and parse; snapshot tests for three hand-written procedures; property
      test: `Parse(Dump(p)) == p` for generated `p`.
- [x] `IrInterpreter` and `ICallOracle`; unit tests per instruction and terminator kind,
      including wrap-around and overflow predicates at width boundaries.
- [x] `tests/Equiv.TestSupport` with `IrGen`; property test: every generated procedure
      validates clean; every `Violations` case fails with the expected id; every
      `Mutation` produces a different interpreter result for some generated input
      (or is discarded by the generator; document the discard rate).
- [x] Row added to `docs/adr/0002-dependencies.md` only if a package is added (none expected).

## Pitfalls
- Do not model `Sort` equality as reference equality in the interpreter; tokens compare by id.
- Signed vs unsigned is on the op, not the type. `IrBitVec` has no sign.
- Keep `IrValue` free of `System.Numerics.BigInteger` in the public API; use `ulong` bits
  plus width.
- Determinism: dump ordering follows block order in the procedure, not a hash set.

## Out of scope
Any Roslyn or Z3 code. Procedure identity normalisation (M1-003).

## Notes
- Decision: non-`Ir` helpers (`ProcedureIdentity`, `CallIdentity`, `SourceSpan`, `ICallOracle`) -> namespace `Equiv.Core`. Alternatives: `Ir`-prefix them, `Equiv.Core.Ir`. Rule: 5 (architecture test requires every `Equiv.Core.Ir` type to start with `Ir`).
- Decision: IR types are `public` (Frontend, Verify and TestSupport consume them). Alternatives: internal + more InternalsVisibleTo. Rule: 1.
- Decision: records holding `ImmutableArray` override `Equals`/`GetHashCode` with sequence equality. Alternatives: custom list wrapper type, test-only comparer. Rule: 3 (`Parse(Dump(p)) == p`).
- Decision: instruction/terminator dispatch via internal abstract visitors. Alternatives: type switches with a default arm. Rule: 2.
- Decision: validator ids IR001-IR010 (IR010 = `Outs` rule from the design). Alternatives: one id per message. Rule: 5.
- Decision: text format writes every definition with its type (`%t1: bv32 = add %a, %t0`), literals are self-typed (`bv32 1`, `bool true`, `sort "S" 3`, `map<k, v> [k -> v] default d`), source names as `%x "x": bv32`, by-ref params as `ref %a: bv32`, exits as `ret %v outs(%a = %a2)`; newlines are whitespace. Alternatives: type inference in the parser. Rule: 4.
- Decision: division by zero and over-wide shifts follow SMT-LIB bitvector semantics (`bvudiv x 0 = ~0`, `bvurem x 0 = x`, shifts >= width give 0 / sign fill). Alternatives: C# semantics. Rule: 1 (Z3 is the consumer; the frontend makes C# behaviour explicit).
- Decision: map value equality is extensional (entries equal to the default are ignored), as in SMT arrays. Rule: 1.
- Decision: `IrRun` (not the oracle) records the call trace; the oracle only answers. Alternatives: stateful oracle. Rule: 4.
- Decision: interpreter validates the procedure and inputs first and throws `ArgumentException` if invalid; reaching `IrUnreachable` yields outcome `IrInfeasible`; the first executed `IrOpaque` stops the run; each executed instruction or terminator costs one step. Rule: 3.
- Decision: phis are forbidden in the entry block (IR006); `ZExt`/`SExt` must widen and `Trunc` narrow (IR007). Rule: 4.
- Decision: one file per type (Meziantou MA0048 is an error under the gates), so "one file per record family" became one flat `src/Equiv.Core/Ir/` folder with a file per type. Nested helper types are also `Ir`-prefixed because the architecture test counts nested types. Rule: 5.
- Decision: `ICallOracle.Answer` (not `Call`: CA1716 reserves `Call` for VB). Rule: 5.
- Decision: `IrGen.Mutation(p)` returns `Gen<IrMutant?>`; null is a discarded edit (no observable difference on 9^k edge inputs plus 16 random ones). The generated return value folds every slot in, which cut the discard rate from about 68% to about 27% (3 runs of 400: 25.5%, 27.3%, 29.2%); `MutationDiscardRateIsReported` prints it and fails above 40%.
- Toolchain: `tools/check-coverage` merged cobertura lines by report-relative filename, but coverlet picks a different `<source>` root per test project (and adds `/_/src/` when it instruments Verify's packages), so Core counted each line two or three times and showed 33%. Fixed in this PR (filenames anchored to their source; user approved), with two new check-coverage tests.
- Toolchain: Verify 33 fails the build with SponsorCheck SC021 until a licence property is set. `Directory.Build.props` claims `SmallRevenue` until 2027-09 (user decision: no revenue yet, monetisation planned). Re-evaluate on monetisation.
- Toolchain: `build.ps1` skips `Equiv.TestSupport` when running tests (it is a library under `tests/`).

