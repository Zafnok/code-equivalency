# async-disposal

`await using` and `await foreach` (ticket P1-029), on .NET 10 on both sides. Each lowers to the calls and
awaits the compiler emits: `DisposeAsync()` and an await of its result on every exit of the `using`, and
`GetAsyncEnumerator`, an awaited `MoveNextAsync()`, `Current` and an awaited `DisposeAsync()` for the loop.
So a pair with the same construct on both sides is compared on the statement inside it.

- `FlushAsync` has an `await using` statement around `count + count` (legacy) and `count * 2` (modern).
- `CloseAsync` has an `await using` declaration and returns `count + 1` (legacy) and `count + 2` (modern).
- `LastAsync` has an `await foreach` whose body assigns `entry + entry` (legacy) and `entry * 2` (modern).
- `FirstAsync` returns from inside an `await foreach`: `entry + 1` (legacy) and `entry - 1` (modern).

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Journal.FlushAsync(IAsyncDisposable, int)` | Equivalent | `bounded` |
| `Journal.CloseAsync(IAsyncDisposable, int)` | Divergent | |
| `Journal.LastAsync(IAsyncEnumerable<int>)` | Equivalent | `lockstep-induction` |
| `Journal.FirstAsync(IAsyncEnumerable<int>)` | Divergent | |

Exit code: 1 (Divergent results).
