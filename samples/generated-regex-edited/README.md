# generated-regex-edited

`generated-regex-unedited` with the pattern edited (ADR 0055, ticket P2-118): the legacy side targets .NET 8 and
has `[GeneratedRegex(@"\s+")]`, the modern side targets .NET 9 and has `[GeneratedRegex(@"[ \t]+")]`.
Nothing else differs.

- `WhiteSpace` is compared by its declaration, and the two attributes differ. `equiv` does not decide
  whether two patterns accept the same language, so the pair is Unknown, on the method somebody wrote.
  It is never Divergent: there is no input to show.
- `Collapse` calls `WhiteSpace` and is the same on both sides, so it is congruent. Its result names
  `WhiteSpace` among the callees it assumed and among those not proved (ADR 0019).
- The matcher is no procedure. `run.properties.generatedProcedures` counts what each side left out.

## Expected verdicts

| Procedure | Verdict | `proofMethod` | `unknownReason` |
|---|---|---|---|
| `Words.WhiteSpace()` | Unknown | | `opaque` (`no-body`) |
| `Words.Collapse(string)` | Equivalent | `congruence` | |

Exit code: 0 (Unknown does not fail the run without `--fail-on unknown`).
