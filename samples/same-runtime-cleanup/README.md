# same-runtime-cleanup

A cleanup commit: both sides target .NET 10 (ADR 0040, ticket P2-055). The pair crosses no runtime, so no
runtime rule applies: no `runtime-changes.json` row, no floating-point to integer rule, no x87 rule.

- `Describe` is byte-identical on both sides. It calls `double.ToString()` and
  `string.StartsWith(string)` and casts a `double` to `int`, each of which differs between
  .NET Framework 4.8 and .NET 10. On one runtime none of them can differ, so the pair is congruent.
- `Rank` is an `if` chain on the legacy side and a `switch` expression on the modern side.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Report.Describe(double, string)` | Equivalent | `congruence` |
| `Report.Rank(int)` | Equivalent | `bounded` |

Exit code: 0 (no Divergent or Unknown result).
