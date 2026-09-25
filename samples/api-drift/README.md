# api-drift

Identical source text binds different members on each side (ADR 0020, ticket M3-009):

- `s.Split(',')` binds `String.Split(params char[])` on .NET Framework 4.8 and
  `String.Split(char, StringSplitOptions)` on .NET 10.
- `s.Contains('x')` binds `Enumerable.Contains<char>` on .NET Framework 4.8 (the legacy side has
  `using System.Linq;`) and `String.Contains(char)` on .NET 10.

The shipped API-equivalence catalogue rewrites each legacy call to its modern member, and each result
lists the entry applied in `properties.equivalencesApplied`. `HasX` stays Divergent on a null `s`,
which is the real behaviour change: the legacy call throws `ArgumentNullException`, the modern one
`NullReferenceException`.

## Expected verdicts

| Procedure | Verdict | `equivalencesApplied` |
|---|---|---|
| `Text.Parts(string)` | Equivalent | `bcl.string-split-one-char` |
| `Text.HasX(string)` | Divergent (on a null `s` only) | `bcl.string-contains-char` |
