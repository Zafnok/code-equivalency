# M3-019 `ref` and `out` arguments to calls, and `lock`
Status: todo
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
4. `lock (o) { ... }` lowers through the CFG with no special case. The whole-body `lock` check is
   deleted.
5. The coverage table rows `Invocation`, `ObjectCreation` and `Lock` are updated. On
   `business-layer`, the TryParse and `lock` methods lower with no opaque. README and census updated.

## Files
`src/Equiv.Core/Ir/IrCall.cs` and the IR infrastructure, `src/Equiv.Frontend.CSharp/Lowering/*`,
`src/Equiv.Verify.Z3/*`, `tests/**`, `docs/tickets/IOPERATION-COVERAGE.md`.

## Tests
`OutArgumentIsACallOutput`, `RefArgumentIsPassedAndReturned`, `TryParseLowersWithoutOpaque`,
`RefToAFieldStaysOpaque`, `LockLowersThroughTryFinally`, a snapshot `TryGetValue`, and the lowering
oracle extended with a `TryParse` case.

## Size guard
If `lock` needs any special-case lowering beyond deleting the whole-body check, stop.

## Out of scope
`ref` locals and `ref` returns. `ref` to fields and array elements.

## Notes
