# cleanup-modern-syntax

A cleanup commit that rewrites old syntax into modern C# (ticket P2-049). Both sides target .NET 10
(ADR 0040), so no runtime rule applies. The legacy side is C# 7.3 (`LangVersion` 7.3) and the modern
side is C# 14. Each method carries one rewrite, and behaves the same on every input:

- `Tidy.Grade`: an `if`/`else if` chain on an `int` becomes a `switch` expression with relational patterns.
- `Tidy.Join`: `string.Format("{0}-{1}", a, b)` on `string` arguments becomes `$"{a}-{b}"`.
- `Tidy.LengthOf`: `x == null ? (int?)null : x.Length` becomes `x?.Length`.
- `Tidy.OrDefault`: `if (x == null) x = d;` becomes `x ??= d;`.
- `Tidy.Measure`: `if (o is Circle) { var c = (Circle)o; ... }` becomes `if (o is Circle c) { ... }`.
- `Circle.Diameter`: a block-bodied getter becomes expression-bodied.
- `Tidy.Positives`: a `foreach` with `if` and `Add` becomes `.Where(...).ToList()`. Both sides keep the
  same guard for a null list, since the loop would throw `NullReferenceException` where `Where` throws
  `ArgumentNullException`.

The snapshot records what `equiv` says today, not what it should say: every pair here is
behaviour-preserving, so every row that is not Equivalent is a gap, and names the ticket that owns it.

## Expected verdicts

| Procedure | Verdict today | Reason | Owner |
|---|---|---|---|
| `Circle.Diameter` (getter) | Equivalent, `proofMethod: bounded` | | |
| `Tidy.Grade(int)` | Equivalent, `proofMethod: bounded` | | |
| `Tidy.Join(string, string)` | Divergent (a precision bug) | legacy calls `String::Format(string,object,object)`, modern lowers to `String::Concat(string,string)`, so the call traces differ | P2-094 |
| `Tidy.LengthOf(string)` | Equivalent, `proofMethod: bounded` | | |
| `Tidy.OrDefault(string, string)` | Equivalent, `proofMethod: bounded` | | |
| `Tidy.Measure(object)` | Equivalent, `proofMethod: bounded` | | |
| `Tidy.Positives(List<int>)` | Unknown (`abstraction`, method scope) | the loop's enumerator and `Add` calls against the uninterpreted `Enumerable::Where` and `ToList`, whose result depends on the lambda | P2-096 |

Exit code: 1 (one Divergent result).

`expected.sarif.json` is the default run, quick mode (ADR 0052).
