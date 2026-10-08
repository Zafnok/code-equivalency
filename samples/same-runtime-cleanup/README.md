# same-runtime-cleanup

A cleanup commit: both sides target .NET 10 (ADR 0040, ticket P2-055). The pair crosses no runtime, so no
runtime rule applies: no `runtime-changes.json` row, no floating-point to integer rule, no x87 rule.

- `Describe` is byte-identical on both sides. It calls `double.ToString()` and
  `string.StartsWith(string)` and casts a `double` to `int`, each of which differs between
  .NET Framework 4.8 and .NET 10. On one runtime none of them can differ, so the pair is congruent.
- `Rank` is an `if` chain on the legacy side and a `switch` expression on the modern side.
- `Native.Beep` is a partial method, byte-identical on both sides, in the shape the interop generator
  gives a `[LibraryImport]` method: its code is in the implementing part, which calls a local `extern`
  function. `equiv` reads the defining declaration and does not lower the other, so both bodies are
  one `no-body` opaque. On one runtime the pair has the fingerprint of the implementing part and of
  its attributes, so it is congruent, and not a changed pair (ADR 0024 as clarified by ticket P2-107).

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Report.Describe(double, string)` | Equivalent | `congruence` |
| `Report.Rank(int)` | Equivalent | `bounded` |
| `Native.Beep(uint, uint)` | Equivalent | `congruence` |

Exit code: 0 (no Divergent or Unknown result).
