# M3-001 Z3 product-program encoder for acyclic IR
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M1-002, M1-003

## Goal
`Equiv.Verify.Z3` implements `IVerificationBackend` for acyclic procedure pairs: encode
both procedures over shared inputs, ask Z3 whether any observable can differ, decode a
model into a readable counterexample, and replay it through `IrInterpreter` to confirm.
Loops are rejected here with `Unknown(loop)`; M3-002 adds the ladder on top of this encoder.

## Spec references
VERIFICATION-MODEL.md sections 1, 5, 6, 7; ADR 0005; ARCHITECTURE.md backend bullets.

## Design

Contract in `Equiv.Core` (M1-003 stubs it; this ticket finalises it):

```
public interface IVerificationBackend
{
    Verdict Verify(ProcedurePair pair, VerificationOptions options);   // options: timeout, bound, call identity map
}
```

Encoding (`ProductEncoder`), one `Z3.Context` and `Solver` per pair, both disposed:

- Sorts: `IrBool` -> `BoolSort`; `IrBitVec(n)` -> `BitVecSort(n)`; `IrSort(name)` -> one
  `UninterpretedSort` per name, shared by both sides; `IrMap(k, v)` -> `ArraySort`.
- Every SSA var becomes a constant named `old.<name>` or `new.<name>`. Parameters are
  shared: `old.param:a == new.param:a` asserted (or one constant reused).
- Instructions become definitional equalities asserted unconditionally: `t = bvadd(a, b)`,
  `t = select(m, k)`, `m2 = store(m, k, v)`. SSA makes this sound: a definition in an
  unreachable block constrains a name nothing reads. `IrOverflows` uses
  `MkBVAddNoOverflow`/`MkBVSubNoUnderflow`/`MkBVMulNoOverflow` and the signed variants,
  negated. `IrUnary` widths via `MkZeroExt`/`MkSignExt`/`MkExtract`.
- Control flow without unrolling: a Bool `reach.<side>.<block>` per block. Entry is true.
  `reach.B = OR over predecessors P of (reach.P AND edge(P -> B))` where `edge` is
  `true` for goto, `cond`/`not cond` for branch arms, the case equality for switch.
  `IrUnreachable` asserts `not reach.B`. Phi: `x = ite(reach.P1 AND edge(P1->B), v1, ite(...))`.
- Observables per side: `returned = OR reach of return blocks`; `ret = ite chain over
  return blocks`; `threw = OR reach of throw blocks`; `exceptionType` an `Int` constant
  per interned type name selected by ite chain; out-params likewise.
- Calls (mutual summaries): per `CallIdentity` and signature, one `FuncDecl`
  `f_callee(args...) -> result` and `f_callee_threw(args...) -> Bool`, shared by both
  sides. Identities present in the config call-identity map are unified before lookup.
  Identities in the runtime-changes table (M2-006) get side-specific functions
  `f_callee_old` / `f_callee_new`, which is what makes them divergent.
- Call trace: a Z3 datatype `Value` with one constructor per IR type in use
  (`ofBool`, `ofBv32`, `ofSortX`, ...; constructors are injective, which is why a
  datatype and not a cast), a datatype `Event(callee: Int, args: Seq<Value>)`, and per
  side a `Seq<Event>` built as the concatenation, in reverse-postorder block order, of
  `ite(reach.B, <events of B in order>, empty)`. Trace equality is one `Seq` equality.
- Query: assert `NOT (returned_old == returned_new AND ret_old == ret_new AND
  threw_old == threw_new AND exceptionType_old == exceptionType_new AND
  outs equal AND trace_old == trace_new)`. `UNSATISFIABLE` -> Equivalent;
  `SATISFIABLE` -> Divergent; `UNKNOWN` -> Unknown(timeout) with the solver reason string.
- `IrOpaque` reaching an observable (compute reachability on the def-use graph) short
  circuits to `Unknown(opaque, reasons)` before encoding.

Counterexample (`ModelDecoder`): read parameter values from `solver.Model` with
`Eval(c, completion: true)`; render bitvectors as signed and unsigned decimals; sorts as
`#n` tokens; produce `Counterexample(inputs, oldOutcome, newOutcome, oldTrace, newTrace)`.
Then replay both procedures in `IrInterpreter` with a call oracle built from the model's
function interpretations (`model.FuncInterp`, default `Else` for unlisted args). If the
replay does not diverge, that is an encoder bug: fail loudly with both results in the
message; do not report Divergent.

## Deliverables
- [ ] `ProductEncoder`, `SortMapper`, `TraceEncoder`, `ModelDecoder`, `Z3Backend`,
      `VerificationOptions`. Z3 objects never escape `Equiv.Verify.Z3`.
- [ ] Unit tests per instruction kind: encoder emits the expected assertion shape
      (compare `Expr.ToString()` snapshots via Verify) and the expected verdict on a
      two-block fixture. Use IR text fixtures parsed with `IrText.Parse`.
- [ ] Fixtures for each observable: return, out param, throw vs no-throw, different
      exception type, same calls different order, extra call, runtime-changed callee.
- [ ] Property (soundness, `Equiv.TestSupport` generators): `Verify(P, P)` is Equivalent
      for 200 generated acyclic P; `Verify(P, Mutate(P))` is never Equivalent; every
      Divergent replays to divergence in the interpreter.
- [ ] Timeout test with a deliberately hard bitvector multiplication fixture and a 50 ms
      timeout produces Unknown(timeout).
- [ ] `docs/adr/0002-dependencies.md` unchanged unless a package is needed (none expected).

## Acceptance criteria (all must hold; nothing beyond them)
1. `IVerificationBackend.Verify` signature is changed once, here, to
   `Verdict Verify(IrProcedure old, IrProcedure @new, VerificationOptions options)`;
   the M1-005 fakes and tests are updated in the same PR.
2. Any procedure containing a back edge returns `Unknown(UnknownReason.Loop, ...)`
   before any Z3 call; the M3-002 ladder replaces this branch.
3. The eight observable fixtures under Tests exist as IR text files under
   `tests/Equiv.Verify.Z3.Tests/Fixtures/` and produce the verdict named in each
   file's first comment line.
4. The soundness property passes 200 cases for `Verify(P, P)` and 200 for
   `Verify(P, Mutate(P))`; every Divergent in those runs replays to divergence in
   `IrInterpreter`.
5. A flagged (`RuntimeChanged`) call on both sides yields `Divergent` with the EQ006
   rule, using side-specific functions.
6. `Z3Backend` disposes its `Context` on every path (a test uses a wrapper counting
   disposals).

## Size guard
Six source files in `src/Equiv.Verify.Z3/`. No abstraction over Z3 (no `ISolver`
interface): the backend is the abstraction.

## Pitfalls
- Z3 `Context` is not thread-safe; never share one across verifications. xUnit v3 runs
  test classes in parallel; a `Context` per test is fine.
- `Microsoft.Z3` 4.12.2 loads `libz3` from `runtimes/<rid>/native`; if the test host
  cannot find it, check the `RuntimeIdentifier`-less build copies it (it should).
- Set timeout with `solver.Set("timeout", (uint)ms)`, not a global param.
- Reverse-postorder is required for the trace concatenation to reflect execution order
  on any path; compute it once per procedure and reuse in M3-002.
- Keep the encoder pure over IR; nothing in it may know about Roslyn.

## Out of scope
Loops, unrolling, induction (M3-002). SARIF (M1-004 already owns it; this ticket returns
`Verdict` objects only).

## Notes
