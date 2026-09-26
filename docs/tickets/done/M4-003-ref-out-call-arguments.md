# M4-003 `ref` and `out` arguments to calls, and `lock`
Status: done (PR #208)
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-005

## Goal
`int.TryParse(s, out var n)`, `Dictionary.TryGetValue(k, out var v)` and `Interlocked.*` appear
everywhere, and all of them are opaque today with reason `ref-argument`. So is every `lock`, because
it desugars to `Monitor.Enter(object, ref bool)`. After this ticket, a call writes each `ref` or
`out` argument through an output of the call, which is a function of the same key as the call's
result (ADR 0018). `lock` then lowers through its `try`/`finally`.

## Spec references
VERIFICATION-MODEL sections 2 and 5; ADR 0018; P1-005's call heap effect (reuse its output
mechanism); `docs/tickets/IOPERATION-COVERAGE.md` rows `Invocation`, `ObjectCreation`, `Lock`; the
`equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. `IrCall` gains `ImmutableArray<IrVar> RefOuts`: the new SSA versions of its `ref` and `out`
   arguments, in parameter order. A `ref` argument's current value is passed as an ordinary
   argument. An `out` argument passes nothing.
2. The encoder defines each output as an uninterpreted function of the call's key, exactly as it
   defines the result, with one function per output index. The replay oracle answers them.
3. The lowerer handles `ref` and `out` arguments that are locals or parameters. A `ref` to a field
   or array element stays opaque with reason `ref-argument`.
4. ~~`lock (o) { ... }` lowers through the CFG with no special case. The whole-body `lock` check is
   deleted.~~ Moved to M4-011 (see Notes, Deviation).
5. The coverage table rows `Invocation`, `ObjectCreation` and `Lock` are updated. On
   `business-layer`, the TryParse method lowers with no opaque (the `lock` one: M4-011). README and census updated.

## Files
`src/Equiv.Core/Ir/IrCall.cs` and the IR infrastructure, `src/Equiv.Frontend.CSharp/Lowering/*`,
`src/Equiv.Verify.Z3/*`, `tests/**`, `docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
`OutArgumentIsACallOutput`, `RefArgumentIsPassedAndReturned`, `TryParseLowersWithoutOpaque`,
`RefToAFieldStaysOpaque`, ~~`LockLowersThroughTryFinally`~~ (M4-011), a snapshot `TryGetValue`, and the lowering
oracle extended with a `TryParse` case.

## Size guard
If `lock` needs any special-case lowering beyond deleting the whole-body check, stop.

## Out of scope
`ref` locals and `ref` returns. `ref` to fields and array elements.

## Notes
- Deviation: criterion 4 (`lock`) moved to new ticket M4-011; the size guard tripped. With `ref` arguments lowered and
  the whole-body check deleted, `lock` lowers to `Monitor.Enter(o, ref lockTaken)` and a `finally` as expected, but
  Roslyn's CFG never assigns the synthesized `lockTaken` local, so the `ref` argument reads an `undefined` value (an
  `IrOpaque "undefined"`). Real code sets it `false` on every entry, a loop iteration's included. Fixing that needs a
  rule for compiler-declared locals (default on region entry), which is special-case lowering beyond deleting the
  check. The check stays, `WholeBodyReasonOwners` names M4-011 for `lock`, and `business-layer`'s `Record` is
  unchanged.
- Deviation: developed on the session's designated branch `claude/ref-out-call-arguments-3806c6` rather than an
  `M4-003-*` branch cut by task-loop step 4; the harness pins the branch name.
- Decision: the new field -> `IrCall.RefOuts` as an `init` property (default empty), like P1-005's `Heap`, so every
  existing `new IrCall(...)` is unchanged. Alternatives: a positional constructor parameter. Rule: 1.
- Decision: text form -> ` refout(%a: T, ...)` after `threw` and before `heap`, omitted when empty, so a dump without
  ref outputs is byte-identical. The word is `refout` because `out` could be read as a return's `outs`. Rule: 5.
- Decision: no new validator rule: a ref output is a definition, so IR003 (redefinition) and dominance already cover it,
  and its type is free. Rule: 3.
- Decision: oracle shape -> `ICallOracle.Answer` gains the ref outputs' types (it cannot answer without them), and
  `IrCallResult.RefOuts` holds one value per output; the interpreter rejects any other count or type. Alternatives: an
  empty answer meaning "default" (a silent value). Rule: 1.
- Decision: encoder names -> `refout:<callee>(<arg sorts>)$<index>:<sort>`, same domain as `f:` (arguments, position,
  heap), shared across sides unless `RuntimeChanged`. The Z3 fixtures `call-refout-same` (Equivalent) and
  `call-refout-index` (Divergent: first vs second output) pin it. Rule: 1.
- Decision: a `ref` argument's value is read at the call, after every other argument is evaluated, since the callee
  reads it through the reference (`P(ref a, a = 5)` passes 5); pinned in `RefArgumentIsPassedAndReturned`. Rule: 3.
- Decision: a discard (`out _`) is an output nobody reads, not an opaque: it writes nothing. Two `ref`/`out` arguments
  that name one variable stay `ref-argument`: which write lands last is the callee's order, and one function per index
  would make `P(out x, out x)` equal `P(out x, out y); x = y` when the callee writes `b` before `a`. Rule: 3.
- Decision: outputs are stored to their variables before the `threw` branch, so a `catch` sees them; they are
  uninterpreted on that path as on the normal one. Rule: 4.
- Decision: a call with `ref`/`out` arguments is not rewritten through an API-equivalence adapter (ADR 0020); adapters
  address by-value source arguments only. Before this ticket such a call never reached the adapter. Rule: 3.
- Decision: the lowering-oracle case -> `Cell.TryParse(int k, out int n)` (`n = k * 3`, returns whether `k` is even),
  generated as `z = o.TryParse(v, out x);`, since `s` is only ever `"s"` or `null`, which `int.TryParse` always
  rejects. Rule: 4.
- `IrGen` programs now pass one slot by `ref` to a call about one call in four, so the round-trip, validator and
  interpreter-vs-model properties cover ref outputs.
- `webapi-basic`'s end-to-end test fails locally (the compare writes no SARIF; its legacy side needs
  `System.Web.Http`), as on `main`; it has no `ref`, `out` or `lock`.
