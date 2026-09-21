# Verification model

This document is the specification the code must satisfy. Tests cite its section numbers.

## 1. What we claim

For a matched procedure pair (P_old, P_new) with the same input signature, we claim
**Equivalent** iff for every input state the observable outputs are equal. Observable
outputs are: the return value, the final values of `ref`/`out` parameters, the final heap
(every field and array slice either side touches; ADR 0018), the sequence of opaque calls
made (callee identity, arguments, and the heap at the call), and whether the procedure
throws (exception type, not message).

Verdicts are modular (ADR 0019). A call to another matched procedure is an uninterpreted
function that both sides share, so a verdict assumes those callee pairs are equivalent. The
SARIF result names them (`assumedCallees`) and flags the ones not proved in the same run
(`unprovenAssumptions`).

Everything else (timing, allocation, log text, exception messages) is not observed.

No single algorithm decides equivalence for every program pair, but this sub-problem
(regression verification: same language family, mostly identical code) is tractable in
practice. Loops are handled by the ladder in section 5.1; a procedure gets **Unknown**
only when every rung fails, the solver times out, or some input reaches an `IrOpaque`
node on either side (ADR 0014). Every result carries `properties.proofMethod` (which rung proved it),
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
| `IrCall(var?, threw?, callee identity, args)` | opaque call; appended to the observable call trace; `threw` is a Bool output. Result, `threw` and heap effect are functions of callee, arguments, the heap at the call and the call's position in the trace (ADR 0018; heap in and out land in P1-005) |
| `IrMapRead(var, map, key)`, `IrMapWrite(newMap, map, key, value)` | SMT `select`/`store`; fields and arrays are maps in SSA like any other value |
| `IrOpaque(var?, reason, sourceSpan)` | frontend could not lower; execution past this point is not modelled, so an input that reaches it has an unknown outcome (ADR 0014) |

Terminators: `IrGoto`, `IrBranch(cond, then, else)`, `IrSwitch`, `IrReturn(var?, outs)`,
`IrThrow(exceptionTypeIdentity, outs)`, `IrUnreachable` (assume false; produced by loop
unrolling, never by the frontend). `outs` names, for every `ref`/`out` parameter, the
SSA version live at that exit; that is how final by-ref values become observables on
both normal and exceptional exits.

Exact record shapes, the text format and the validator rules are specified in ticket
M1-002 and pinned by its snapshot tests.

Null: reference-typed values are a `Sort` plus a separate `Bool` "is null" shadow
variable. A dereference lowers to a conditional `IrThrow(NullReferenceException)`.

Heap and nullness are inputs (M2-004). A procedure's parameter list is its C# parameters
followed by the synthesised inputs its body needs, ordered by name: the receiver `this`,
one `null.<Sort>` map from a reference sort to Bool, one `field.<Type>.<Field>` map per
field touched, and `array.<v>` plus `length.<v>` per array variable indexed. They are
parameters of the same name on both sides, so the product encoding (M3-001) shares their
inputs exactly as it shares the C# parameters. A value's shadow is a `mapread` of `null.<Sort>`,
so equal references are equally null; `new` sets the shadow to false instead. `this`,
`null.*` and `length.*` are `In`, because nothing changes them. `field.*` and `array.*` are
`Ref` (ADR 0018, ticket M3-007), so every exit names their final version in `outs` and the
final heap is an observable like any `ref` parameter. When only one side of a pair has a given
`Ref` map, the other side never touches that slice, and the encoder compares the first side's
final value against the shared input. IR variable names take only letters, digits, `_`, `.`
and `$`, which is why these names are spelled with dots.

Two gaps the M2-004 heap model leaves open, stated here so a later ticket does not assume
otherwise (ADR 0015). ADR 0018 schedules both fixes, and M3-007's, before M3-003, so no
build that reports sample verdicts carries them. An `IrCall` does not havoc any `field.*` map, so a call's effect on the
heap is not modelled and a pair that differs only in where it reads a field around a call is not
distinguished; ticket P1-005 closes this. And `array.<v>` is keyed per array *variable*, not per
array value, so two variables holding the same array are two independent slices; ticket P1-006
closes this. Both follow the M2-004 acceptance criteria, both are unsound in general, and both can
only produce a false Equivalent, silently: no `IrOpaque`, no `properties.opaqueNodes` entry, no
Unknown. Until P1-005 and P1-006 land, M3-001's soundness harness (section 7) is not evidence that
the C# frontend is sound.

## 3. Lowering rules (C#)

Source of truth is Roslyn `ControlFlowGraph.Create` over `IOperation`. The Roslyn CFG
already desugars `foreach`, `using`, `lock`, `?.`, `??`, pattern matching, string
interpolation and `try`/`finally` into explicit blocks. That is why we lower from the
CFG instead of walking syntax: syntactic sugar is gone before we see it. What we add is
SSA renaming, type narrowing, opaque-call identity, and explicit exception edges.

C# integer semantics the lowering makes explicit (M2-003; `char` is bv16, ADR 0013):

- Checked `+ - *` and unary `-` test `IrOverflows` and branch to one shared
  `System.OverflowException` throw block. A checked explicit integral conversion throws
  when the value does not round-trip, or when exactly one side is signed and the
  signed-side value is negative.
- `/` and `%` test for a zero divisor (`System.DivideByZeroException`). Signed `/` and
  `%` also test `IrOverflows sdiv` (`System.OverflowException`) whether or not the code
  is checked, because .NET throws on `MinValue / -1` and `MinValue % -1` in both contexts.
- A shift count is masked to `width - 1` before the IR shift, as C# does.
- An opaque call's `threw` flag branches to `IrThrow("System.Exception")`. A `throw`
  statement is `IrOpaque` in v1, because the thrown object's dynamic type is not known
  statically.

Migration-specific normalisations (applied to both sides before matching):

- `System.Web` vs `Microsoft.AspNetCore` attribute routes map to one route identity.
- `HttpResponseMessage` / `IHttpActionResult` vs `IActionResult` map to one result
  identity (status code observed, body opaque). This is done by `api-equivalences.json`
  type and member entries (ADR 0020, ticket M3-009), not by a separate normaliser.
- Namespace and type rename maps come from `equiv.config.json`.
- BCL API changes are NOT auto-equated (`WebClient` vs `HttpClient` calls are different
  identities and therefore Divergent unless the user maps them). False alarms are
  cheaper than false proofs. The one exception is a shipped, cited catalogue
  (`api-equivalences.json`, ADR 0020, ticket M3-009) of member and type pairs that are
  exactly equivalent whenever both are invoked: overload drift such as
  `String::Split(Char[])` → `String::Split(Char, StringSplitOptions)`, and Web API 2 →
  ASP.NET Core result helpers and result types. The frontend rewrites a legacy call while
  lowering it, with an argument adapter, and every entry applied to a pair is listed in
  `properties.equivalencesApplied`. Users can suppress entries in `equiv.config.json`.
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
threw flag, exception type, final heap), check. A call's result, `threw` flag and heap
effect are uninterpreted functions of (identity, arguments, heap at the call, position),
where the position is the number of calls the same side made before it (ADR 0018). Calls
at the same position with the same identity, arguments and heap therefore agree across
sides. Two calls on one side are never forced to agree, because a real callee may be
stateful. The exception is an identity in the runtime-changes table, which gets
side-specific functions. The call trace is a bounded list compared element-wise; an event
is (identity, arguments, heap at the call).

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
| Unknown | none (rule default `warning`) | `open` | EQ003 (reason: timeout, opaque, unmatched overload, loop until the M3-002 ladder) |
| Added | none (rule default `note`) | `informational` | EQ004 |
| Removed | none (rule default `note`) | `informational` | EQ005 |
| Divergent (runtime-changed API) | `error` | `fail` | EQ006 (breaking-change link in `message`) |

Every verdict on a matched pair with bodies also carries `properties.assumedCallees` and
`properties.unprovenAssumptions` (ADR 0019), and `properties.equivalencesApplied` when a
catalogue entry fired (ADR 0020).

Baseline: SARIF `baselineState` (`new`, `unchanged`, `updated`, `absent`) computed from
a result fingerprint (procedure identity + verdict + model hash). The exit code considers
only `new` results unless `--no-baseline` is given. Accepting a divergence as the new
behaviour is done by committing the SARIF file as the baseline, nothing more.

`new` versus `updated` is decided by procedure identity *and* rule id (the verdict's kind,
EQ001-EQ005): a rule-id change for an identity already in the baseline — e.g. Equivalent
(EQ001) regressing to Divergent (EQ002) — is always `new`, so the exit code never misses it.
`updated` is reserved for a same-rule-id fingerprint change (e.g. a different counterexample
on a procedure that was already Divergent); that distinction is not exit-code-significant, so
it does not depend on the "model hash" half of the fingerprint being identical across runs of
the same underlying divergence.

`level` is only meaningful on a result when `kind` is `fail` (SARIF 2.1.0 s3.27.9), so
EQ003-EQ005 results carry `level: none`; the parenthesised value is the rule's
`defaultConfiguration.level`, severity metadata only. Whether a consumer renders Unknown with
a badge is not guaranteed; the gate for Unknown is `--fail-on unknown`. See ADR 0011.

## 7. Test obligations derived from this document

- Soundness harness (property test, `Equiv.Verify.Z3.Tests`): for any generated IR
  procedure P, `verify(P, P)` is Equivalent; for P and a random semantics-changing
  mutation P', the verdict is Divergent or Unknown, never Equivalent. Mutations include
  dropping or changing a map write (the final heap is observable) and duplicating a call
  whose results are compared (calls are not idempotent; ADR 0018). Runs against
  every ladder rung independently. It generates IR, so it covers the encoder and the
  ladder only: a C#-to-IR lowering gap is invisible to it by construction, and the two
  section 2 heap gaps are exactly that (ADR 0015). The obligation that covers C#-to-IR is
  the lowering oracle below, which P1-005 and P1-006 each extend with the case that
  catches its own gap.
- Ladder monotonicity (property test): a pair proved on rung n is never refuted on
  rung m; a counterexample from rung 1 replays to Divergent in the IR interpreter.
- Lowering oracle (property test, `Equiv.Frontend.CSharp.Tests`): for generated
  straight-line integer methods, compile and run the C# in memory and run the IR via
  `IrInterpreter` (production code in Core, also used to replay counterexamples);
  outputs agree.
- Snapshot tests (Verify): IR dump and SARIF for every sample in `samples/`.
- Every row in the tables above has at least one unit test named after it.
