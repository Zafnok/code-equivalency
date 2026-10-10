# runtime-row-constant-args

The same calls on .NET Framework 4.8 and on .NET 10 (ticket P2-073). `Calls.cs` is the same text on
both sides. Every method calls a member with a row in `runtime-changes.json`, and each of those rows
has a `precondition`: it applies to a call only when the call's compile-time constant arguments can
reach the change the row documents.

- `Words` splits on the constant pattern `\s+`. The `Regex` row is about case-insensitive matching,
  and the pattern does not ignore case, so the row does not apply.
- `Cache` combines two path literals with no character .NET Framework's path validation rejects, so
  the path-validation row does not apply.
- `Letters` splits on `(?i)[a-z]`, which ignores case: the row applies.
- `Piped` combines the literal `a|b`, which .NET Framework rejects and .NET 10 accepts: the row applies.
- `SplitBy` and `Under` pass a parameter where the others pass a constant. An argument that is not a
  constant is not checked, so the rows apply as they did before the ticket.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Calls.Words(string)` | Equivalent | `congruence` |
| `Calls.Cache()` | Equivalent | `congruence` |
| `Calls.Letters(string)` | Divergent (EQ006, a runtime-changed API) | none |
| `Calls.Piped()` | Divergent (EQ006, a runtime-changed API) | none |
| `Calls.SplitBy(string, string)` | Divergent (EQ006, a runtime-changed API) | none |
| `Calls.Under(string)` | Divergent (EQ006, a runtime-changed API) | none |

Exit code: 1 (a new Divergent result).
