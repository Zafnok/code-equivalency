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
(`unprovenAssumptions`). This holds for cycles too: if every pair on a cycle of matched
procedures is Equivalent, every pair is partially equivalent (the mutual-summary rule).

Everything else (timing, allocation, log text, exception messages) is not observed.

No single algorithm decides equivalence for every program pair, but this sub-problem
(regression verification: same language family, mostly identical code) is tractable in
practice. Loops are handled by the ladder in section 5.1; a procedure gets **Unknown**
only when every rung fails, the solver times out, some input reaches an `IrOpaque` node
that is not shared by both sides (ADRs 0014 and 0024), every divergence found depends on
an abstraction (ADR 0026), or the method's bound body is erroneous (`Unbound`, ADR 0029). Every
Unknown states its scope. A `line` Unknown is still a proof about every input that reaches none
of the lines it lists; a `method` Unknown claims nothing (ADR 0029). A pair whose bound bodies fingerprint equal and are not
runtime-sensitive is Equivalent by congruence, without the solver (`proofMethod:
congruence`, ADR 0024): identical bound code makes the same claim a shared call does. Every result carries `properties.proofMethod` (which rung proved it),
`properties.boundedBy` when the claim is bounded, and `properties.opaqueNodes`, so a
reader can see exactly how strong the claim is. Never report Equivalent without saying how.

## 2. IR

Procedure = signature + ordered basic blocks + entry block. Block = instructions +
terminator. SSA: every `IrVar` is assigned once; blocks with several predecessors use
`IrPhi`.

Types: `Bool`; `BitVec(n)` for integral types (n in 8, 16, 32, 64, signedness kept on
the operation, not the type); `Sort(name)` for everything else (strings, objects,
decimals, floats), treated as uninterpreted with equality only; `Map(key, value)` for
SSA heap slices (one map per field, one per array sort) encoded as SMT arrays. Floating
point is a `Sort` in the MVP (not IEEE-modelled); a post-MVP ticket exists. Operators on
floating point, `decimal` and user-defined operators are `IrPure` applications of named
functions both sides share (ADR 0025, ticket M4-002), so unchanged arithmetic is provable
without modelling its semantics.

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
| `IrCall(var?, threw?, callee identity, args, refouts, heap)` | opaque call; appended to the observable call trace; `threw` is a Bool output. `refouts` are the new versions of the call's `ref` and `out` arguments, in parameter order, each a definition; a `ref` argument's value at the call is also one of `args`, an `out` one's is not (M4-003). `heap` lists, per by-ref map the call reads and writes, the map's name, the version before the call (a use) and the version after it (a definition); the C# frontend lists every `field.*` and `array.*` map the body touches, at every call, since which fields a callee reaches is not known without a call graph (P1-005). Result, `threw`, each ref output (one function per output index) and each map's new version are functions of callee, arguments, the heap at the call and the call's position in the trace (ADR 0018) |
| `IrMapRead(var, map, key)`, `IrMapWrite(newMap, map, key, value)` | SMT `select`/`store`; fields and arrays are maps in SSA like any other value |
| `IrPure(var, throws, function, args)` | applies a catalogued pure function (`f64.add`, `dec.mul`, `op:<identity>`); no trace event, no heap, no position; each entry of `throws` is a Bool output branching to an `IrThrow` of its exact exception type; shared by both sides except runtime-sensitive functions, which are side-specific (ADR 0025) |
| `IrOpaque(var?, reason, sourceSpan, fingerprint?, reads)` | frontend could not lower; execution past this point is not modelled, so an input that reaches it has an unknown outcome (ADR 0014), unless the same `fingerprint` occurs on the other side, in which case both occurrences are one call `opaque:<fingerprint>` over `reads` (ADR 0024) |

Terminators: `IrGoto`, `IrBranch(cond, then, else)`, `IrSwitch`, `IrReturn(var?, outs)`,
`IrThrow(exceptionTypeIdentity, outs)`, `IrUnreachable` (assume false; produced by loop
unrolling, never by the frontend). `outs` names, for every `ref`/`out` parameter, the
SSA version live at that exit; that is how final by-ref values become observables on
both normal and exceptional exits.

Exact record shapes, the text format and the validator rules are specified in ticket
M1-002 and pinned by its snapshot tests.

Null: reference-typed values are a `Sort` plus a separate `Bool` "is null" shadow
variable. A dereference lowers to a conditional `IrThrow(NullReferenceException)`, placed
where the CLR checks it: at the field or element load or store, or at the call, after every
operand evaluated before it (index, arguments, a stored value) (P2-017).

Heap and nullness are inputs (M2-004). A procedure's parameter list is its C# parameters
followed by the synthesised inputs its body needs, ordered by name: the receiver `this`,
one `null.<Sort>` map from a reference sort to Bool, one `field.<Type>.<Field>` map per
field touched, `array.<Sort>` from an array reference to its elements by bv32 index plus
`length.<Sort>` from an array reference to its length, per array sort indexed, keyed by the
reference like `null.<Sort>` so that two variables holding one array share its elements (P1-006), and one
`cast.<From>.<To>` map from `<From>`'s IR type to `<To>`'s sort per implicit reference or boxing
conversion between different IR types (M3-010), one `istype.<From>.<To>` map from `<From>`'s sort to Bool per
reference type test (M4-005), one `typeof.<T>` input of `System.Type` sort
per closed type `T` a body reads with `typeof(T)` (P2-002), and one `new.<Sort>` from bv32 to an
array sort per array sort a body creates (P2-001). An array creation `new T[n]` (one `int` dimension)
throws `System.OverflowException` when `n` is negative, then reads its reference from `new.<Sort>` at
the body's count of that sort's creations so far (0 for the first), writes `n` into `length.<Sort>`
and a constant map of `default(T)` into `array.<Sort>` at that reference, and stores an initialiser's
values at indices 0, 1, ... in order; its shadow is false. `new.<Sort>` is shared by name, so both
sides' k-th creations of a sort are one reference; nothing keeps it apart from the arrays the inputs
reach, which only adds inputs, never removes a real run. A cast map is an uninterpreted function with no
trace event: the same operand always converts to the same value. The converted value's nullness is
read from `null.<To>` like any value's, not tied to the operand's, which over-approximates (a real
upcast of a non-null value is never null). A type test of a reference `x` against a reference type `T` (`x is T`,
a type or declaration pattern, `x as T`, a downcast `(T)x`) is `!isNull(x) && istype.<From>.<T>[x]`: `istype` is a
free predicate per pair of types, shared by both sides by name, and nothing ties it to the type hierarchy. `x is T t`
binds `t` to `cast.<From>.<T>[x]`, never null; `x as T` is that cast when the test passes and `null` otherwise, and
its nullness is the test's negation; `(T)x` branches to `IrThrow("System.InvalidCastException")` when the test fails
on a non-null `x`, is `null` for a null `x`, and the cast otherwise. Unboxing, a test of or against a type parameter,
and a test between types no reference conversion relates stay opaque. `typeof(T)` for an open generic or method type parameter
`T` stays `IrOpaque("TypeOf")`; for a closed `T` it reads `typeof.<T>` directly, is never null and
adds no trace event. The product
encoding (M3-001, ADR 0021) shares the C# parameters by position, because that is how a caller
binds them, and the synthesised inputs by name; two parameters of different types are never
shared, each is then an input of its own side. A synthesised input's name is `this` or contains
a dot, and a C# parameter's never does: that is how the encoder tells them apart (`IrParameterNames.IsSynthesised`
is the one definition). A C# parameter whose name would be synthesised, which can only be one declared `@this`,
is spelled with a leading `$` in IR (`$this`; its source name stays `this`). No C# identifier contains `$`, so
that name is never another parameter's (M3-007). A value's shadow is a `mapread` of `null.<Sort>`,
so equal references are equally null; `new` sets the shadow to false instead. `this`,
`null.*`, `cast.*`, `istype.*`, `length.*`, `typeof.*` and `new.*` are `In`, because nothing changes them, except
that `length.<Sort>` is `Ref` in a body that creates an array of that sort (P2-001; ADR 0018 clarification). No CLR array has a
negative length, so the encoder assumes every read of a `length.*` input is non-negative, whether or not the read is
reached (the CLR never reads a null reference's length, so this drops no input a caller can pass), and the model
decoder gives 0 wherever a model's length map is negative, which can only be at a reference nothing reads (P2-019). `field.*` and `array.*` are
`Ref` (ADR 0018, ticket M3-007), so every exit names their final version in `outs` and the
final heap is an observable like any `ref` parameter. When only one side of a pair has a given
`Ref` map, the other side never touches that slice, and the encoder compares the first side's
final value against the shared input. IR variable names take only letters, digits, `_`, `.`
and `$`, which is why these names are spelled with dots.

Two gaps the M2-004 heap model left open (ADR 0015), both closed before M3-003 as ADR 0018
schedules. The first, a call that neither read nor wrote the heap, so that a pair differing only in
where it reads a field around a call was not distinguished, is closed by P1-005: every `IrCall`
reads and writes each `field.*` and `array.*` map its procedure touches. The second gap, array maps keyed per array *variable*
so that two variables holding one array were two independent slices, is closed by P1-006: the
element and length maps are keyed by the array reference.

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
- An opaque call's `threw` flag branches to `IrThrow("System.Exception")`. `throw new T(...)`
  records the constructor call and then lowers to `IrThrow("T")` on T's static type (M2-004).
  Throwing any other expression is `IrOpaque` with reason `Throw`, because the thrown object's
  dynamic type is not known statically, and `throw;` is `IrOpaque` with reason `rethrow`.

An `async` method (ticket M4-006) is lowered as the synchronous body its CFG already is, over the original
operations, never over the compiler's state machine. `await e` is `IrCall(await:<awaiter type>, [e])`: its result is
the awaited value and its `threw` flag branches as any call's. The awaiter type is spelled as a member identity spells
its declaring type, with its type arguments as a generic callee's; a reference-typed `e` whose `GetAwaiter` is an
instance method is null-checked first, as a `callvirt` receiver is. The procedure returns the task's result: the type
argument of a generic task-like return type, nothing for `Task`, `ValueTask` or `void`. Why this is sound for a pair
where both sides are `async`: every observable of section 1 happens in the same order whether or not the body is
suspended at an `await` (the heap is threaded through the call, so what other code does meanwhile is a havoc both sides
share), and an exception thrown anywhere in the body, before the first `await` or after one, is caught by the method's
builder and stored in the returned task as it is, faulted (cancelled for `OperationCanceledException`), the same way on
both sides; so a throw of type `T` in the IR stands for exactly the task a caller observes. Two awaits are two calls at
different trace positions (ADR 0018), so awaiting one task twice is not forced to yield one value, and a real awaitable
that is not idempotent is modelled. `ConfigureAwait(false)` is an ordinary call whose result is what is awaited. A pair
where exactly one side is `async` is Unknown with detail `async-mismatch`, without the solver: a synchronous method
throws to its caller at the call, an `async` one into its task, which the caller sees only when it awaits, so their
exception timing differs. Iterators (`yield`, async or not), `await foreach` and `await using` stay whole-body opaque with
reasons `iterator`, `await-foreach` and `await-using`: their desugaring is a state machine or awaits calls the CFG does
not show.

Floating-point, `decimal` and user-defined operators (ADR 0025, ticket M4-002) are `IrPure`
applications of the functions one frontend catalogue lists: `f32.<op>` and `f64.<op>` (arithmetic,
negation and comparisons, which never throw; `==` is `f64.eq`, not an equality of sort elements, since
`NaN != NaN`), `dec.<op>` (overflow on `+ - * / %`, divide-by-zero on `/ %`), and `conv.<from>.<to>`
for every numeric conversion to or from `float`, `double` or `decimal` (overflow for floating point
to `decimal`, for `decimal` to an integral type, and for floating point to an integral type when
checked). Unary `+` is its operand. A user-defined operator or conversion, including `string ==`, is
`op:<call identity>`, which may throw any exception, as an opaque call may. Each exception flag
branches to where that exact type goes, so `catch (OverflowException)` catches `dec.mul`'s overflow
and `catch (DivideByZeroException)` does not. A floating-point to integer conversion, and on a legacy
project whose floating point runs on x87 every function taking or yielding floating point, is
runtime-sensitive. Lifted (nullable) operators, compound assignment and `++`/`--` on these types stay
`IrOpaque`.

Migration-specific normalisations (applied to both sides before matching):

- `System.Web` vs `Microsoft.AspNetCore` attribute routes map to one route identity.
- `IHttpActionResult` vs `IActionResult` map to one result identity (status code observed,
  body opaque). This is done by `api-equivalences.json` type and member entries (ADR 0020,
  ticket M3-009), not by a separate normaliser, and like every catalogue entry it is applied
  to the legacy side only. `HttpResponseMessage` has no entry: an action returning it stays
  Divergent unless the user maps it.
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
side-specific functions. An `IrOpaque` whose fingerprint occurs on both sides is encoded as
exactly such a call, with identity `opaque:<fingerprint>` and its reads as arguments (ADR
0024). An `IrPure` is a function of its arguments only, shared unless runtime-sensitive (ADR
0025). The call trace is a bounded list compared element-wise; an event
is (identity, arguments, heap at the call). The heap at a call ranges over every map a heap pair
names on either side, in name order (P1-005). A call reads a map it pairs at its `before`; any
other map it reads at the version the encoder threads through that side's calls, which starts at
the shared input and is replaced by each call's new version of the map. A side that does not have
the map as a parameter reports that threaded version as its final value.

### 5.1 Loop ladder

Loops and recursion are tried on each rung in order; the first rung that proves
Equivalent or finds a counterexample wins, and `proofMethod` names it.

| Rung | Method | Claim | When it applies | Ticket |
|---|---|---|---|---|
| 1 | Bounded unrolling, k iterations, `assume false` on the last back edge; self-calls inlined k deep | Divergent with a concrete trace; Equivalent (`boundedBy: k`) only when no input reaches the bound | always; runs first because counterexamples surface at small k | M3-002 |
| 2 | Lockstep relational induction (mutual summaries): align loop pairs by position in the loop nesting forest and pair each header's state; cut both sides at every header and prove, from equal inputs and from each pair of equal header states, that both sides reach the same next header with equal states or leave with equal observables | **unbounded** Equivalent | both sides have the same loop forest and pairable header states; covers unchanged and cosmetically changed loops | M3-002 |
| 3 | k-induction: rung 2 with k prior iterations assumed equal | unbounded Equivalent | bodies agree only after warm-up | M3-002 |
| 4 | Constrained Horn clauses solved by Z3 Spacer: each loop is a recursive predicate, Z3 synthesises the coupling invariant | unbounded Equivalent, or Unknown(chc-timeout) | loops do not align (loop to LINQ, fusion, iterator rewrite) | P1-001 |
| 5 | LLM-proposed coupling invariant checked by Z3; a wrong guess can never yield Equivalent | unbounded Equivalent, or Unknown(no-invariant) | rung 4 timed out | P1-002 |

Recursion is handled by rung 2 with the recursive call as the induction point
(the standard regression-verification treatment): a self-call stays a call both sides share. Rung 1 inlines it
instead, so its counterexamples are real. Mutual recursion needs no rung: a call to another matched procedure is
shared (section 1, ADR 0019), so `Recursion` means only a self-call that no rung decided.

Rung 1's result is a proof only when no input reaches the bound; otherwise it only refutes, and rungs 2 and 3
decide. A pair with loops or a self-call that no rung decides is Unknown: `Opaque` when a failed obligation reaches
an `IrOpaque`, `Recursion` when a side calls itself, `UnalignedLoop` when the loops do not align or neither
induction proves them, `Timeout` when only the solver gave up. A header's state is its phis plus every other value
live on entry to it (loop-invariant values, heap maps, values used after the loop). A rung 2 obligation's model is a
counterexample only when it comes from the base (real inputs) and replays to a divergence through the original
procedures; a step's model may start from an unreachable state. Every result lists the rungs it ran, with their
outcomes, in `properties.ladderTrace`. Partial equivalence is what every
rung proves; termination is compared separately as an observable only when both sides
have a syntactic termination argument (bounded counters), otherwise not claimed.

## 6. Verdict semantics and SARIF mapping

| Verdict | SARIF `level` | `kind` | ruleId |
|---|---|---|---|
| Equivalent | none | `pass` | EQ001 |
| Divergent | `error` | `fail` | EQ002 (counterexample in `properties.model` and in `message`) |
| Unknown | none (rule default `warning`) | `open` | EQ003 (reason in `properties.unknownReason`: timeout, opaque, unmatched-overload, unaligned-loop, recursion, abstraction, unbound) |
| Added | none (rule default `note`) | `informational` | EQ004 |
| Removed | none (rule default `note`) | `informational` | EQ005 |
| Divergent (runtime-changed API) | `error` | `fail` | EQ006 (breaking-change link in `message`) |

Every verdict on a matched pair with bodies also carries `properties.assumedCallees` and
`properties.unprovenAssumptions` (ADR 0019), and `properties.equivalencesApplied` when a
catalogue entry fired (ADR 0020).

A counterexample is replayed in `IrInterpreter` with taint (ADR 0026): results of `IrPure`
and of `opaque:` calls are tainted, and so is an `opaque:` call's own trace event, since it stands for the
fragment's calls. Taint follows data, and a branch on a tainted value taints the rest of that side. The result is Divergent only when a compared observable differs and is
untainted on both sides. Otherwise it is Unknown with reason `Abstraction`, carrying the model
as `properties.candidateCounterexample` and the abstractions it depends on as
`properties.abstractions`.

With `equiv compare --execute`, every Divergent is also replayed on the two real runtimes, the
second oracle of ADR 0035 (decision 2; ticket M4-009). Its model's inputs are bound back to each
side's parameters by the product's pairing rule (ADR 0021) and become C# arguments of the M3-032
generator types: a `bool`, an integer, a `char` or an enum from its bitvector; a `string` from its
sort element, `null` where the model's `null.<Sort>` map holds it and otherwise `"s<id>"`, so that
equal elements are equal strings; a `float`, `double` or `decimal` as the number `id`; any other
reference type only as `null`. A static method is called directly, and an instance method on
`new T()`, which needs a public parameterless constructor. Each side's project is emitted with its
references beside it, and a driver calls the legacy method once on .NET Framework 4.8 and the
modern method once on .NET 10, under the invariant culture. The result carries
`properties.replay`:
- `reproduced`: the two canonical outcomes (M3-032's canonical form) differ;
- `not-reproduced`: they are equal, and `properties.replayOutcomes` gives both (`kind`, `value`).
  The model and the CLR disagree; in a corpus run that is a soundness or modelling finding and
  gets a ticket;
- `not-constructible`, with `properties.replayReason`: the method is not public, generic, an
  accessor other than a getter, or takes a parameter by reference; the receiver has no public
  parameterless constructor or the model makes it null; a parameter's type has no generator; the
  model has a synthesised input other than `this` and `null.*` (a heap map, a cast or type-test
  map, `typeof`, `new`), since replay builds no object graphs; a project does not emit
  (`emit-failed`); the model's two runs end alike, so the divergence is in the call trace, which a
  driver does not observe; or a side gives no comparable outcome.

Replay never changes the verdict, the rule id, the fingerprint or the exit code, and a run without
`--execute` runs no code and writes no `replay`.

An Unknown result lists every reached opaque node and every abstraction it depends on as a
`relatedLocation` whose message is the reason, each line once, legacy side first and then in source order. Its
primary location is the first of them on the modern side, else the procedure (ADR 0027). `partialFingerprints` do
not change with it: an opaque Unknown's detail names each `side: reason` once, without lines.

Every Unknown carries `properties.scope` (ADR 0029):
- `line`: every cause is a span inside the method, and the first query of ADR 0014 was
  unsatisfiable. The result also carries `properties.residualClaim: "equivalent unless a
  relatedLocation is reached"`, and its message says so.
- `method`: a whole-body opaque, a timeout, an exhausted loop ladder, an unmatched overload, or
  `Unbound` code. So is any Unknown on a pair with a loop or self-call: rung 1 runs the first query
  on the unrolled pair, which proves nothing past the bound. An `abstraction` Unknown is `method`
  too, since the first query found its candidate, so that query was satisfiable (ticket M3-025).

The IR text spells a whole-body opaque `opaque body "reason"` (`IrOpaque.WholeBody`), so scope is
read from the IR rather than guessed from the span.

A whole-body opaque's span is the construct that caused it (the `foreach`, the `lock`, the filtered
`catch`), not the method body. A method whose bound body holds a compiler error, an
`IInvalidOperation` or an error-type symbol is `Unknown(Unbound)`, with the diagnostics as its
causes, and is never Equivalent by congruence. Neither scope nor causes is part of the
fingerprint.

Every run writes `run.properties.loweringCensus`: procedures per side, matched pairs, pairs
without `IrOpaque`, whole-body opaque pairs, congruent pairs, and `IrOpaque` counts by reason
per side (ADR 0027). It also records skipped projects per side, and, when the run produced
verdicts, Unknown counts by scope (ADR 0029).

Only the projects a solution builds are part of the product. For a `.sln`, those are the projects
with a `Build.0` entry for its default configuration (`Debug|Any CPU`, else the first one it
lists); a `.slnx`, or a `.sln` that lists no configuration, builds all of them. The others are
never opened, so they are neither loaded nor skipped, and every run names them once per side in
`run.properties.projectsNotBuilt` (`legacy`, `modern`). The project load rate (ADR 0028) is C#
projects loaded over C# projects built: skipped projects count against it, projects not built do
not (ticket P2-013).

Counts in the census are per lowered body of a matched pair. `procedures` counts, per side, the
matched pairs plus the removed (legacy) or added (modern) procedures. `opaqueByReason` counts the
bodies on each side that hold at least one `IrOpaque` with that reason, sorted by reason. A body is
whole-body opaque when it is one block whose only instruction is an `IrOpaque`, and a pair counts
under `pairsWholeBodyOpaque` when either side is (ticket M3-014). A matched pair whose lowering threw has
no lowered body, so it counts in `procedures` and `matchedPairs` but in neither
`pairsWithoutOpaque` nor `pairsWholeBodyOpaque`, nor in `opaqueByReason` (ticket P2-011).

The census also counts what the solver will see (ADR 0034; ticket M3-030). A lowered matched pair
is *changed* unless it is congruent: both bound fingerprints are equal, neither is runtime-sensitive,
and neither body is unbound (ADRs 0024 and 0029; ticket M3-015). `pairsCongruent` counts the
congruent pairs. `changedPairs`, `changedPairsWithoutOpaque` and
`changedPairsWholeBodyOpaque` are `matchedPairs`, `pairsWithoutOpaque` and `pairsWholeBodyOpaque`
restricted to changed pairs; lowerable share is `changedPairsWithoutOpaque / changedPairs`.
`changedReasonSets` maps the sorted, `+`-joined union of both sides' opaque reasons to its number
of changed pairs, with `""` for a pair without opaque, so its counts sum to `changedPairs`.
`runtimeChangeCalls` has `callSites`, `distinctMembers` and `pairsWithAny`, each per side and over
every lowered matched pair, congruent ones included: the `IrCall`s whose callee identity the table
matches, the distinct callee identities among them, and the pairs whose body on that side has at
least one. Package version changes are not in the census; `tools/corpus/corpus.ps1 -Packages`
computes them from each side's restore output.

`externalCallees` (ADR 0035; ticket M3-033) is every BCL member a lowered body calls, not only the
ones `RuntimeChangeTable` already lists: per side, over every lowered matched pair (congruent ones
included, as `runtimeChangeCalls` counts), the distinct call identities whose target assembly is one
of the framework reference assemblies the project compiled against (a reference assembly carries
`ReferenceAssemblyAttribute`, as `ProjectEmitter` already tests for replay), never the solution's own
code or a NuGet package. Each entry pairs a member with its call-site count, sorted by count
descending then ordinally. `tools/corpus/corpus.ps1 -RuntimeDiff <slug>` takes the union of both
sides' most-called entries and runs `tools/runtime-diff` on each; a member it finds divergent becomes
a `runtime-changes.json` row with `source: measured` and a witness.

Every run also writes `run.properties.analysedLinesOfCode`: `legacy` and `modern`, one count per
codebase and never a total (ticket M3-014). The rule is the one in README's "Licence" section,
applied identically to both sides. It counts the lines that hold part of a C# token, in every C#
file the loaded projects compile (generated files included), and a file compiled by more than one
project once. `--dry-run` prints the same two numbers without verifying.

A pair whose verification throws (an encoder bug, a `Z3Exception`), or whose lowering throws
(ticket P2-011), has no result: a crash is
a fact about the tool, not a verdict about the code (ADR 0023). It is recorded as an `error`
entry in `invocations[0].toolExecutionNotifications` naming both identities, the invocation
has `executionSuccessful: false`, the run's `properties.unverified` lists the pair's identity,
and the other pairs are reported as usual. Its baseline result, if any, is carried through as
`unchanged` with `properties.unverified: true`, never as `absent`, so a crash cannot make a
known divergence look fixed.

A project that cannot be loaded is contained the same way, one level up (ADR 0029). A C# project
that fails to load or has unresolved references, and any project that is not C#, is skipped. It
gets a tool-execution notification (`error` for C#, `warning` otherwise), and its procedures are
listed in `properties.unverified`. Their baseline results are carried as `unchanged` with
`properties.unverified: true`. No Added or Removed result is reported for a procedure whose
counterpart project, matched by assembly name, was skipped on the other side. Every other project
is analysed and reported as usual.

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
  ladder only: a C#-to-IR lowering gap is invisible to it by construction, and the
  section 2 heap gaps are exactly that (ADR 0015). The obligation that covers C#-to-IR is
  the lowering oracle below, which P1-005 and P1-006 each extend with the case that
  catches its own gap.
- Ladder monotonicity (property test): a pair proved on rung n is never refuted on
  rung m; a counterexample from rung 1 replays to Divergent in the IR interpreter.
- Lowering oracle (property test, `Equiv.Frontend.CSharp.Tests`): for generated
  straight-line integer methods, compile and run the C# in memory and run the IR via
  `IrInterpreter` (production code in Core, also used to replay counterexamples);
  outputs agree.
- Differential soundness (property test, M0-012; `DifferentialSoundnessTests` in
  `Equiv.Tests.Integration`): the two obligations above check the encoder against IR and
  the lowering against IR, so neither sees a false Equivalent that enters between C# and
  the verdict. This one closes that loop on generated code. `PairGen` generates a C#
  method and a second one derived from it by one mutation operator, from a preserving
  family or a changing family; both are compiled and run on the CLR, and the real
  frontend and Z3 backend verify the pair. Over each pair and its inputs (CsCheck's, and
  the model of a Divergent verdict):
  1. if any input gives different observables (return value, exception type, field and
     array state), the verdict is not Equivalent;
  2. if the verdict is Divergent, replaying its model in C# gives different observables;
  3. if the operator is preserving, the verdict is not Divergent.

  Rule 1 is the soundness rule; rules 2 and 3 are the decoding and precision rules. A
  changing operator can produce an equivalent mutant, so no rule assumes a mutant differs.
  200 pairs per PR, 5,000 nightly.
- Snapshot tests (Verify): IR dump and SARIF for every sample in `samples/`.
- Congruence (property test, ADR 0024): whenever congruence reports Equivalent on a
  generated or sample pair, the solver on the same pair never reports Divergent.
- Taint (ADR 0026): no Divergent result's differing observable is tainted.
- Containment (ADR 0029): a solution with one unloadable project reports every other project's
  results, and an unbound method is Unknown(Unbound), never congruent. Residual claim (property
  test): for a `line`-scoped Unknown, every generated input on which neither side reaches a listed
  cause gives equal observables in `IrInterpreter`.
- Every row in the tables above has at least one unit test named after it.
