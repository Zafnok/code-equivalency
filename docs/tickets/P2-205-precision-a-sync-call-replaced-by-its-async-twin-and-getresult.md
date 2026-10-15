# P2-205 Precision bug: a synchronous call replaced by its `Async` twin and `GetAwaiter().GetResult()` is Divergent
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-192

## Goal
A migration to a library whose synchronous API is gone replaces `c.Foo(x)` by
`c.FooAsync(x).GetAwaiter().GetResult()`. P2-130's adjudication of `duplicati-3124`
(`docs/runs/2026-10-14-migrations-verdict.md`, row 20) found
`Duplicati.Library.Backend.S3IAM::CanCreateUser(string,string)` a false EQ002 of this shape: the AWS SDK
calls `SimulatePrincipalPolicy` and `GetUser` became their `Async` twins with `GetResult()`. The same file
has three more such calls (`CreateUser`, `PutUserPolicy`, `CreateAccessKey`).

ADR 0059 (P2-192) lets a user declare one callee the same function as another, one call for one call. This
is a different kind: one legacy call against two modern calls, the second on the first's result. The
config's call-identity map cannot say it, and the result would need the `Task` the first call returns to be
the legacy call's result wrapped.

When done, such a pair can be declared (or catalogued) and is decided, and what keeps the declaration
from hiding a real difference is decided, as ADR 0059 did for one call for one call.

## Spec references
ADR 0059, ADR 0020, ADR 0019, `docs/tickets/done/P2-192-*.md`, `.claude/skills/equiv-adr`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, through `equiv-adr`, how `Foo(args) = FooAsync(args).GetAwaiter().GetResult()` is declared and
   checked: for example a `callIdentityRenames`-like entry whose modern side names the `Async` member and
   the `GetResult` step, or a frontend rewrite of the modern pattern into one call. Say what an exception
   the task completes with becomes (`GetResult` throws it unwrapped, as the synchronous call would).
2. Implement it with a sample pair, one of whose callers awaits a different member and stays Divergent.
3. Rerun `duplicati-3124` in compare mode quick with the declaration; Notes give `CanCreateUser`'s result.

## Tests
As the decision requires; a pair whose modern side calls `.Result` or `Wait()` in place of
`GetAwaiter().GetResult()` is not covered (they wrap the exception in `AggregateException`) and stays Divergent.

## Size guard
An `await` in place of `GetResult()` (the caller becomes `async`): stop, that is the async mismatch of ticket
M4-006, not this ticket.

## Out of scope
Base class library sync and async pairs the catalogue (ADR 0020) could hold.

## Notes
- Filed 2026-10-15 by P2-192 (its size guard: a second kind of equivalence).
