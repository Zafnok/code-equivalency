# M4-006 `await` in async methods as a call
Status: in-progress
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M4-001

## Goal
ASP.NET Core controllers and services are overwhelmingly `async`, so a migration that also adds
`async` gets nothing proved while `await` is opaque. After this ticket, `await e` in a method that is
`async` on both sides lowers to an `IrCall` with identity `await:<normalised awaiter type>`, the
awaitable as its argument, the awaited value as its result, and a `threw` edge. The method body is
otherwise lowered as a synchronous body whose return value is the task's result.

## Spec references
VERIFICATION-MODEL sections 1 (observables), 2 and 5; ADR 0018 (position keying makes repeated
awaits sound); the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. For a pair where both sides are `async` methods, `IAwaitOperation` lowers as the Goal states. A
   thrown exception is the same observable as a synchronous throw. Both sides wrap it in a faulted
   task the same way, and the spec's Why says so.
2. A pair where exactly one side is `async` gets Unknown with detail `async-mismatch`, without
   calling the backend. Its sync and async exception timing differ.
3. `ConfigureAwait(false)` is an ordinary call on the awaitable and needs no special case.
4. Iterators (`yield`) and `await foreach` / `await using` stay whole-body opaque with their own reasons.
5. Coverage table row `Await` is added. On `business-layer`, the `async` method lowers with no
   opaque. README and census updated.

## Files
`src/Equiv.Frontend.CSharp/Lowering/*`, `src/Equiv.Cli/CompareCommand.cs` (the `async-mismatch`
check, if the frontend cannot express it), `docs/tickets/IOPERATION-COVERAGE.md`, `tests/**`.

## Tests
`AwaitIsACallOnTheAwaitable`, `TwoAwaitsOfTheSameTaskAreNotForcedEqual`,
`AsyncMismatchIsUnknown`, `ConfigureAwaitIsAnOrdinaryCall`, `IteratorStaysOpaque`, a snapshot
`AsyncControllerAction`.

## Size guard
No `Equiv.Core` change beyond one `UnknownReason` detail string. If the state machine leaks into
the CFG, stop: Roslyn's CFG for an async method is over the original operations.

## Out of scope
Sync-to-async migrations (they are `async-mismatch`). `ValueTask` pooling. Iterators.

## Notes
- Decision: where `async-mismatch` is decided -> the frontend, which alone sees both symbols, replaces both bodies of a pair
  with exactly one `async` side by one whole-body `IrOpaque` with reason `Unknown.AsyncMismatchReason`, and `CompareCommand`
  turns that into `Unknown(Opaque, "async-mismatch")` before congruence and the backend, as it does `unbound`.
  Alternatives: an `IsAsync` flag on `ProcedurePair` (a Core change beyond the one string), leaving it to the backend (calls
  it). Rule: 4.
- Decision: the await's identity -> `await:` + the awaiter type's name as a member identity spells its type (rename map,
  `` `n `` arity) + `<type arguments>` when constructed, as `CallIdentityFactory` suffixes a generic callee. Alternatives:
  Roslyn's display string, the awaitable's type. Rule: 1.
- Decision: a reference-typed awaitable whose `GetAwaiter` is an instance method is null-checked before the call, as a
  `callvirt` receiver is (P2-017). Alternatives: no check (the call's `threw` of unknown type covers it). Rule: 1.
- Decision: an async method's IR return type -> the one type argument of its generic task-like return type; none for
  `Task`, `ValueTask`, `void` or any other non-generic one. Alternatives: the task's sort. Rule: 1.
- Decision: whole-body reasons `iterator` (any method with `yield`, async or not), `await-foreach`, `await-using`, each
  spanning the first offending construct (ADR 0029 decision 3); iterators are checked first. The await forms are only
  looked for in an `async` method, so an async lambda in a sync method does not make it whole-body opaque. Rule: 5.
- Decision: `async-mismatch` wins over `unbound`: it is decided from the two signatures before either body. Rule: 4.
