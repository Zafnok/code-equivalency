# runtime-row-sync-position

A version upgrade between two .NET versions: the legacy side targets .NET 5 and the modern side .NET 6
(ADR 0040 decision 2, ticket P2-114). `Position.cs` is byte-identical on both sides.

- `Rest` reads from a `FileStream` with the synchronous `Read` and then reads its `Position`. The row
  `System.IO.FileStream::get_Position(` (`changedIn: net6.0`) records that .NET 6 moves `Position` when
  `ReadAsync` or `WriteAsync` is issued, not when it completes. The row `requires` an asynchronous stream
  operation in the same method, and `Rest` has none, so the row does not fire and the pair is congruent.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Position.Rest(FileStream)` | Equivalent | `congruence` |

Exit code: 0 (no Divergent or Unknown result).

## The same method with an asynchronous read

`async-variant/` is the pair with `s.ReadAsync(b, 0, b.Length).GetAwaiter().GetResult()` in place of `Read`.
The method now issues an asynchronous operation on the stream before it reads `Position`, so the row fires and
the method is Divergent (EQ006). The integration test `RuntimeRowSyncPosition_AnAsyncReadKeepsTheRow` runs that
pair; it has no snapshot.
