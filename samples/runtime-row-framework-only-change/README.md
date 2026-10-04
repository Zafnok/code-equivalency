# runtime-row-framework-only-change

A version upgrade between two .NET versions: the legacy side targets .NET 8 and the modern side .NET 9
(ADR 0040 decision 2, ticket P2-113). `Text.cs` is byte-identical on both sides.

- `Same` calls `string.Equals(string, string, StringComparison)`, whose row in `runtime-changes.json` has
  `changedIn: net5.0` (ICU replaced NLS on Windows in .NET 5).
- `Enc` reads `Encoding.Default`, whose row has `changedIn: netcoreapp1.0` (UTF-8 on every .NET (Core)).

Both sides already have both changes, so neither row applies and each pair is congruent.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Text.Same(string, string)` | Equivalent | `congruence` |
| `Text.Enc()` | Equivalent | `congruence` |

Exit code: 0 (no Divergent or Unknown result).

## The same file from .NET Framework 4.8

`net48/legacy/` is an old-style .NET Framework 4.8 project that compiles `legacy/Text.cs` itself. Against
`modern/` the pair crosses both change points, so both methods are Divergent (EQ006). The integration test
`RuntimeRowFrameworkOnlyChange_FromNet48KeepsBothRows` runs that pair; it has no snapshot.
