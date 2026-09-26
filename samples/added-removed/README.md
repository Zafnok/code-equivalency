# added-removed

`Calculator.Add` exists unchanged on both sides. `LegacyOnly` exists only on the legacy
side; `ModernOnly` exists only on the modern side. Exercises `ProcedureEnumerator`/
`StableIdentityMatcher` end to end (ticket M2-002): one `Added` and one `Removed`
identity, each with a `physicalLocation` pointing at its declaring file and line.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Calculator.Add(int, int)` | Equivalent |
| `Calculator.LegacyOnly(int)` | Removed |
| `Calculator.ModernOnly(int)` | Added |

Exit code: 0 (no Divergent or Unknown result; Added and Removed do not affect the exit code).
