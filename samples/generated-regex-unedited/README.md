# generated-regex-unedited

A version upgrade with a `[GeneratedRegex]` method nobody edited: the legacy side targets .NET 8 and the
modern side .NET 9 (ADR 0055, ticket P2-118). `Words.cs` is byte-identical on both sides.

The regex source generator writes the implementing part of `WhiteSpace` and, in
`System.Text.RegularExpressions.Generated`, a matcher type and its helpers. The .NET 8 and the .NET 9
generator write different code for the same pattern.

- `WhiteSpace` is compared by its declaration: its signature and its `[GeneratedRegex]` attribute, which
  are equal, so the pair is congruent. Both sides still lower to one `no-body` opaque.
- `Collapse` calls `WhiteSpace` and `Regex.Replace` and is the same on both sides, so it is congruent.
- The matcher is no procedure. `run.properties.generatedProcedures` counts what each side left out.

Before ADR 0055 `WhiteSpace` was Unknown (`no-body`), and every method of the matcher was a pair of
its own: congruent where the two generators agree, Unknown or Divergent where they do not.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Words.WhiteSpace()` | Equivalent | `congruence` |
| `Words.Collapse(string)` | Equivalent | `congruence` |

Exit code: 0 (no Divergent or Unknown result).
