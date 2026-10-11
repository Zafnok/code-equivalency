# runtime-row-valid-path-source

The same calls on .NET Framework 4.8 and on .NET 10 (ticket P2-074). `Paths.cs` is the same text on
both sides. Every method calls a member with a path-validation row in `runtime-changes.json`: .NET
Framework rejects a path with an invalid character up front, and .NET 10 does not. Such a row does
not apply to a call whose path the method itself shows to be valid, by the `validPaths` lists in the
table's header.

- `BaseDir` takes the directory of `Assembly.Location`, a valid-path source: the runtime made that
  path, so it has no character the validation rejects.
- `Load` opens `p` only where `File.Exists(p)` returned true. `File.Exists` is a validating guard: it
  returns false, and does not throw, for a path the validation rejects.
- `Under` passes its parameter straight to `Path.Combine`. A path from a caller is not followed, so
  the row applies as it did before the ticket.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Paths.BaseDir()` | Equivalent | `congruence` |
| `Paths.Load(string)` | Equivalent | `congruence` |
| `Paths.Under(string)` | Divergent (EQ006, a runtime-changed API) | none |

Exit code: 1 (a new Divergent result).
