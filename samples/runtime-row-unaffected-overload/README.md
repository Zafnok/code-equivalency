# runtime-row-unaffected-overload

A runtime-changes row flags a call only where the call reaches the change (ticket P2-075). `legacy/Text.cs` and
`modern/Text.cs` are byte-identical; the legacy side is .NET Framework 4.8 and the modern side .NET 10, so the pair
crosses the `net5.0` row that moved culture comparisons from NLS to ICU.

- `Has(string)` calls `s.IndexOf("x.exe", StringComparison.OrdinalIgnoreCase)`. An ordinal comparison does not use ICU
  or NLS, so the row does not apply to it and the pair is congruent. Equivalent.
- `HasCulture(string)` is the same call with `StringComparison.CurrentCultureIgnoreCase`, which both runtimes compare
  with their own culture data. The row applies. Divergent, EQ006.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Text.Has(string)` | Equivalent (congruence) |
| `Text.HasCulture(string)` | Divergent (EQ006) |

Exit code: 1 (a new Divergent result).
