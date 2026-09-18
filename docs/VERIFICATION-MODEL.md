# Verification model

This document is the specification the code must satisfy. Tests cite its section numbers.

## 1. What we claim

For a matched procedure pair (P_old, P_new) with the same input signature, we claim
**Equivalent** iff for every input state the observable outputs are equal. Observable
outputs are: the return value, the final values of `ref`/`out` parameters, the sequence
of opaque calls made (callee identity plus arguments), and whether the procedure throws
(exception type, not message).

Everything else (timing, allocation, log text, exception messages) is not observed.

No single algorithm decides equivalence for every program pair, but this sub-problem
(regression verification: same language family, mostly identical code) is tractable in
practice. Loops are handled by the ladder in section 5.1; a procedure gets **Unknown**
only when every rung fails, the solver times out, or an `IrOpaque` node flows into an
output. Every result carries `properties.proofMethod` (which rung proved it),
`properties.boundedBy` when the claim is bounded, and `properties.opaqueNodes`, so a
reader can see exactly how strong the claim is. Never report Equivalent without saying how.

## 2. IR

Procedure = signature + ordered basic blocks + entry block. Block = instructions +
terminator. SSA: every `IrVar` is assigned once; blocks with several predecessors use
`IrPhi`.

Types: `Bool`; `BitVec(n)` for integral types (n in 8, 16, 32, 64, signedness kept on
the operation, not the type); `Sort(name)` for everything else (strings, objects,
decimals, floats), treated as uninterpreted with equality only; `Map(key, value)` for
SSA heap slices (one map per field, one per array) encoded as SMT arrays. Floating
point is a `Sort` in the MVP (not IEEE-modelled); a post-MVP ticket exists.

IR instructions never throw. Every exception edge is explicit in the CFG: the frontend
lowers `checked` arithmetic to an overflow test plus a branch to a throw block, and a
call that may throw yields a Bool that the frontend branches on.

Instructions:

| Instruction | Meaning |
|---|---|
| `IrConst(var, value)` | literal |
| `IrBinary(var, op, a, b)` | arithmetic, bitwise, comparison; wrapping semantics; signedness is part of `op` |
| `IrOverflows(var, overflowOp, a, b)` | Bool: would the checked operation overflow; `overflowOp` in SAdd, UAdd, SSub, USub, SMul, UMul, SDiv |
| `IrUnary(var, op, a)` | negation, not, conversions with explicit target width and signedness |
| `IrPhi(var, [(block, var)])` | SSA merge |
| `IrCall(var?, threw?, callee identity, args)` | opaque call; appended to the observable call trace; `threw` is a Bool output |
| `IrMapRead(var, map, key)`, `IrMapWrite(newMap, map, key, value)` | SMT `select`/`store`; fields and arrays are maps in SSA like any other value |
| `IrOpaque(var?, reason, sourceSpan)` | frontend could not lower; poisons every dependent value |

Terminators: `IrGoto`, `IrBranch(cond, then, else)`, `IrSwitch`, `IrReturn(var?, outs)`,
`IrThrow(exceptionTypeIdentity, outs)`, `IrUnreachable` (assume false; produced by loop
unrolling, never by the frontend). `outs` names, for every `ref`/`out` parameter, the
SSA version live at that exit; that is how final by-ref values become observables on
both normal and exceptional exits.

Exact record shapes, the text format and the validator rules are specified in ticket
M1-002 and pinned by its snapshot tests.

Null: reference-typed values are a `Sort` plus a separate `Bool` "is null" shadow
variable. A dereference lowers to a conditional `IrThrow(NullReferenceException)`.

## 3. Lowering rules (C#)

Source of truth is Roslyn `ControlFlowGraph.Create` over `IOperation`. The Roslyn CFG
already desugars `foreach`, `using`, `lock`, `?.`, `??`, pattern matching, string
interpolation and `try`/`finally` into explicit blocks. That is why we lower from the
CFG instead of walking syntax: syntactic sugar is gone before we see it. What we add is
SSA renaming, type narrowing, opaque-call identity, and explicit exception edges.

Migration-specific normalisations (applied to both sides before matching):

- `System.Web` vs `Microsoft.AspNetCore` attribute routes map to one route identity.
- `HttpResponseMessage` / `IHttpActionResult` vs `IActionResult` map to one result
  identity (status code observed, body opaque).
- Namespace and type rename maps come from `equiv.config.json`.
- BCL API changes are NOT auto-equated (`WebClient` vs `HttpClient` calls are different
  identities and therefore Divergent unless the user maps them). False alarms are
  cheaper than false proofs.
- Runtime-changed APIs: a shipped data table (`runtime-changes.json`, sourced from
  Microsoft's .NET Core 3.0 to .NET 10 breaking-changes list) names BCL members whose
  behaviour differs between .NET Framework and .NET even when the call is textually
  identical (ICU vs NLS string comparison and `IndexOf`, x87 vs SSE floating point on
  x86, `GetHashCode` randomisation, serialization defaults). A matched pair of calls to
  such a member is never treated as the same uninterpreted function; it produces
  Divergent with ruleId EQ006 and a link to the breaking-change entry. Users may
  suppress per member in `equiv.config.json`.

## 4. Matching

Identity = assembly-agnostic namespace + type + member name + normalised parameter
types. Present on one side only means `Added` or `Removed` (SARIF level `note`).
Ambiguous overload mapping means `Unknown`.

## 5. Encoding

Product program: declare inputs once, inline P_old and P_new with disjoint SSA names,
assert that some observable differs (disjunction over return, out params, call trace,
threw flag, exception type), check. Calls with the same identity and equal arguments
return equal values on both sides (uninterpreted functions) unless the identity is in
the runtime-changes table. The call trace is a bounded list compared element-wise.

### 5.1 Loop ladder

Loops and recursion are tried on each rung in order; the first rung that proves
Equivalent or finds a counterexample wins, and `proofMethod` names it.

| Rung | Method | Claim | When it applies | Ticket |
|---|---|---|---|---|
| 1 | Bounded unrolling, k iterations, `assume false` on the last back edge | Equivalent up to k (`boundedBy: k`), or Divergent with a concrete trace | always; runs first because counterexamples surface at small k | M3-002 |
| 2 | Lockstep relational induction (mutual summaries): align loop pairs by CFG position and normalised guard; assume equal states at loop entry, prove bodies produce equal states and equal guards | **unbounded** Equivalent | both sides have a loop at the same position; covers unchanged and cosmetically changed loops | M3-002 |
| 3 | k-induction: rung 2 with k prior iterations assumed equal | unbounded Equivalent | bodies agree only after warm-up | M3-002 |
| 4 | Constrained Horn clauses solved by Z3 Spacer: each loop is a recursive predicate, Z3 synthesises the coupling invariant | unbounded Equivalent, or Unknown(chc-timeout) | loops do not align (loop to LINQ, fusion, iterator rewrite) | P1-001 |
| 5 | LLM-proposed coupling invariant checked by Z3; a wrong guess can never yield Equivalent | unbounded Equivalent, or Unknown(no-invariant) | rung 4 timed out | P1-002 |

Recursion is handled by rung 2 with the recursive call as the induction point
(the standard regression-verification treatment). Partial equivalence is what every
rung proves; termination is compared separately as an observable only when both sides
have a syntactic termination argument (bounded counters), otherwise not claimed.

## 6. Verdict semantics and SARIF mapping

| Verdict | SARIF `level` | `kind` | ruleId |
|---|---|---|---|
| Equivalent | none | `pass` | EQ001 |
| Divergent | `error` | `fail` | EQ002 (counterexample in `properties.model` and in `message`) |
| Unknown | `warning` | `open` | EQ003 (reason: timeout, opaque, unmatched overload) |
| Added | `note` | `informational` | EQ004 |
| Removed | `note` | `informational` | EQ005 |
| Divergent (runtime-changed API) | `error` | `fail` | EQ006 (breaking-change link in `message`) |

Baseline: SARIF `baselineState` (`new`, `unchanged`, `updated`, `absent`) computed from
a result fingerprint (procedure identity + verdict + model hash). The exit code considers
only `new` results unless `--no-baseline` is given. Accepting a divergence as the new
behaviour is done by committing the SARIF file as the baseline, nothing more.

## 7. Test obligations derived from this document

- Soundness harness (property test, `Equiv.Verify.Z3.Tests`): for any generated IR
  procedure P, `verify(P, P)` is Equivalent; for P and a random semantics-changing
  mutation P', the verdict is Divergent or Unknown, never Equivalent. Runs against
  every ladder rung independently.
- Ladder monotonicity (property test): a pair proved on rung n is never refuted on
  rung m; a counterexample from rung 1 replays to Divergent in the IR interpreter.
- Lowering oracle (property test, `Equiv.Frontend.CSharp.Tests`): for generated
  straight-line integer methods, compile and run the C# in memory and run the IR via
  `IrInterpreter` (production code in Core, also used to replay counterexamples);
  outputs agree.
- Snapshot tests (Verify): IR dump and SARIF for every sample in `samples/`.
- Every row in the tables above has at least one unit test named after it.
