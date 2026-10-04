# P1-029 `await using` and `await foreach` lower through their awaits instead of making the body opaque
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-006, M4-001

## Goal
M4-006 lowers `await e` as the call `await:<awaiter type>`. It left two constructs whole-body
opaque, with reasons `await-using` and `await-foreach`, because their desugaring awaits calls the
CFG does not show: `DisposeAsync()` at the end of an `await using`, and `MoveNextAsync()` and
`DisposeAsync()` in an `await foreach`. A whole-body opaque is a `method` scoped Unknown, the kind
that claims nothing. On `jellyfin-13023` the reason `await-using` is in 83 bodies per side and alone
keeps 13 of 573 changed pairs (2.3%) opaque, with no owner. Server code on modern .NET is where
these constructs live, and it is what a version upgrade touches.

When done, both constructs lower to the calls and awaits the compiler emits, in order, and the two
reasons are gone except for the cases the Size guard names.

## Spec references
VERIFICATION-MODEL.md section 3 (the `async` paragraph; `using` and `foreach` through the CFG,
M4-001), ADR 0018 (calls and positions), ADR 0029 (whole-body opaques),
`docs/tickets/IOPERATION-COVERAGE.md`, `docs/tickets/done/M4-006-await-as-a-call.md`,
`src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (where the two reasons are raised).

## Acceptance criteria (all must hold; nothing beyond them)
1. `await using (var x = e) { body }`, and the declaration form, lower as the synchronous `using`
   does, with the disposal being the call `DisposeAsync()` on the resource followed by an await of
   its result (`await:<awaiter type>`), on both the normal and the exceptional exit, and skipped
   for a null resource of a reference type.
2. `await foreach (var x in e) { body }` lowers as the enumerator loop `foreach` does, with
   `GetAsyncEnumerator(...)`, an awaited `MoveNextAsync()`, `get_Current`, and an awaited
   `DisposeAsync()` in the `finally`. A `WithCancellation` or `ConfigureAwait` on the collection is
   an ordinary call whose result is enumerated.
3. Two bodies with the same `await using` or `await foreach` and a changed statement inside are
   compared on that statement: a new sample `async-disposal` has one Equivalent pair and one
   Divergent pair for each construct. Snapshots checked in.
4. A pair whose legacy side has `using` and whose modern side has `await using` is not Equivalent:
   the two make different calls.
5. On a `--lower-only` run of `jellyfin-13023`, the reason `await-using` is in at most 10 bodies per
   side, or Notes records what the rest are.
6. `IOPERATION-COVERAGE.md` and VERIFICATION-MODEL.md section 3 are updated, and the lowering
   oracle's generator gains both constructs if it can express them; if not, Notes says why.

## Files
`src/Equiv.Frontend.CSharp/Lowering/` (the lowerer and its exception collaborator), its tests,
`samples/async-disposal/**`, `docs/tickets/IOPERATION-COVERAGE.md`, `docs/VERIFICATION-MODEL.md`.

## Tests
`AwaitUsing_DisposesThroughAnAwaitedCall`, `AwaitUsing_DisposesOnTheThrowingExit`,
`AwaitUsing_SkipsANullResource`, `AwaitForeach_AwaitsMoveNextAndDisposes`,
`AwaitForeach_WithCancellationIsACallOnTheCollection`, `UsingAgainstAwaitUsing_IsNotEquivalent`, and
the `async-disposal` snapshot.

## Size guard
Pattern-based disposal on a `ref struct`, an `await using` over several resources in one statement
if the CFG does not order them, and the IL lowering: leave them opaque with their reason and say so
in Notes. Iterators (`yield`) are not this ticket.

## Out of scope
Iterators and async iterators as producers (reason `iterator`). The compiler's state machine.
`ConfigureAwait` semantics beyond "an ordinary call".

## Notes
- From the 2026-10-03 improvement review (its first priority names async/await). Filed directly
  rather than through P1-028 because the constructs and their desugaring are already known.
