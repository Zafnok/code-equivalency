# P2-114 The `FileStream.Position` row fires only where the stream had an asynchronous read or write
Status: done (PR #455)
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
The row `System.IO.FileStream::get_Position(` (changedIn `net6.0`) records that .NET 6 moves
`Position` when `ReadAsync` or `WriteAsync` is issued, not when it completes. A read of `Position`
can only see the change after an asynchronous operation on the same stream. On P2-066's
`gitextensions-9860` (net5.0 to net6.0), `ConEmu.WinForms.AnsiLog::PumpStream()` is EQ006 under this
row. It reads `Position` and calls the synchronous `Read`, and no member of the class issues an
asynchronous operation on the stream, so the difference cannot be reached. Minimal repro, as a sample
pair (identical file; legacy `net5.0`, modern `net6.0`):

```csharp
static long Rest(System.IO.FileStream s) { var b = new byte[16]; s.Read(b, 0, b.Length); return s.Length - s.Position; }
```

Today this is EQ006. It should be congruent. Fire the row only when the method that reads
`Position` also calls `ReadAsync`, `WriteAsync`, `BeginRead`, `BeginWrite`, `CopyToAsync` or
`FlushAsync` on a `FileStream` or `Stream`. A `Position` read on a stream that came from outside the
method, in a method with no such call, may still follow an asynchronous call in its caller. Decide
through `equiv-decide` whether that case fires, and log the `Decision:` line.

## Spec references
VERIFICATION-MODEL section 3 (runtime-changed APIs), `runtime-changes.json`, P2-073 (a row
precondition on the call's arguments), `docs/runs/2026-10-03-upgrade-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `samples/runtime-row-sync-position` (the pair above) has no EQ006.
2. The same method with `ReadAsync` in place of `Read` stays EQ006.

## Tests
Integration test on the sample, both variants.

## Out of scope
Other rows that depend on an earlier call on the same receiver: list any you find in `## Notes`.

## Notes
- Found by P2-066: 1 on `gitextensions-9860`.
- Decision: a `Position` read in a method with no asynchronous stream call does not fire the row, even when the stream came
  from outside the method (a parameter, a field). The ticket's own repro takes the stream as a parameter and must be
  congruent, so a possible async call in a caller cannot keep the row. The residual unsoundness is a caller that issues
  `ReadAsync` and passes the stream to a method that only reads `Position`.
- Decision: the precondition is a row field `requires` (value `asyncStreamOperation`, enum `RowPrecondition`), checked per
  method: a method that does not meet it has the row's member added to its suppressed runtime changes before it is lowered
  or fingerprinted (`RowPreconditions.Suppress`), so no call-identity signature changed. P2-073's argument preconditions are
  a different field and do not use this one.
- Decision: "a call on a `FileStream` or `Stream`" is any invocation whose target method's containing type is
  `System.IO.Stream` or derives from it, so a `Stream` subclass's own `ReadAsync` override counts; an extension method does not.
- The IL fallback lowering (`IlLowerer`) does not apply the precondition (it already passes no suppressions): a `Position`
  read in IL, i.e. in a callee reached without source, is still flagged, which is the conservative side.
- Other rows that depend on an earlier call on the same receiver (out of scope, not changed): `System.Reflection.FieldInfo::SetValue(`
  (after the type is initialized), `System.Security.Cryptography.CryptoStream::Dispose(` (after a partial read),
  `System.Linq.Enumerable::First(` (after `OrderBy`), `System.Security.Cryptography.Oid::set_Value(` (a second assignment),
  `System.Net.Sockets.Socket::get_LocalEndPoint(` (after an implicit bind by `SendToAsync`),
  `System.Net.Security.SslStream::BeginAuthenticateAs` (a second call before the first completes) and
  `System.Net.WebClient::CancelAsync(` (once the response is being read). `requires` can carry each as a new `RowPrecondition` value.
