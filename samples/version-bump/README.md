# version-bump

A version upgrade: the legacy side targets .NET 8 and the modern side .NET 10 (ADR 0040, ticket P2-055).
The pair crosses .NET 9 and .NET 10, so only the runtime rules whose change point is one of those two
apply. Both methods are byte-identical on the two sides.

- `Name` calls `BinaryReader.ReadString()`, whose row in `runtime-changes.json` has `changedIn: net9.0`.
  The row lies inside the interval, so the pair is not congruent and the two calls are different
  functions.
- `Format` calls `double.ToString()`, whose row has `changedIn: netcoreapp3.0`. Both sides already have
  that change, so the row does not apply and the pair is congruent.

## Expected verdicts

| Procedure | Verdict | Rule |
|---|---|---|
| `Codec.Name(BinaryReader)` | Divergent (runtime-changed API, between net8.0 and net10.0) | EQ006 |
| `Codec.Format(double)` | Equivalent (`proofMethod: congruence`) | EQ001 |

Exit code: 1 (a new Divergent result).
