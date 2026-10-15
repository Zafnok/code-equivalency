# append-interpolated-string

An interpolated string passed straight to `StringBuilder.Append` or `AppendLine` across .NET 5 and .NET 6
(ticket P2-164). `Text.cs` is byte-identical on both sides. The legacy side targets .NET 5 (C# 9), where the
string is built first and `Append(string)` or `AppendLine(string)` takes it. The modern side targets .NET 6
(C# 10), where the call binds `Append(ref StringBuilder.AppendInterpolatedStringHandler)`, and the compiler
calls the handler's `AppendLiteral` or `AppendFormatted` once per part, each writing into the builder.

The text appended is the same, and every hole here is a `string` or an `int` parameter, which runs no code and
cannot throw. So nothing can tell the handler's piece-by-piece writes from one write of the joined string, and
the modern side lowers to the legacy side's call. The bound trees still differ, so no pair is congruent: each
is proved by the solver.

Before P2-164 each method was Divergent (EQ002): the two call traces differed, and the counterexample had the
modern side throw inside the handler.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Text.Header(System.Text.StringBuilder, string)` | Equivalent | `bounded` |
| `Text.Count(System.Text.StringBuilder, int)` | Equivalent | `bounded` |
| `Text.Line(System.Text.StringBuilder, string)` | Equivalent | `bounded` |

Exit code: 0 (no Divergent or Unknown result).
