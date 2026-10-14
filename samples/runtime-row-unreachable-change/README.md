# runtime-row-unreachable-change

The same calls on .NET Framework 4.8 and on .NET 10 (ticket P2-181). `Calls.cs` is the same text on
both sides, so every method is a body nobody edited. Each calls a member with a row in
`runtime-changes.json`, and each row's change is reachable only from some arguments: the row's
`precondition` says which. A body whose calls cannot reach any row's change is congruent; one whose
calls can is not, and a runtime rule keeps it out of congruence.

- `Cache` combines two path literals with no character .NET Framework's path validation rejects, so
  the path-validation row cannot reach it.
- `Slash` searches for the character `/`. A search for a character is ordinal on every runtime, so
  the ICU row cannot reach it whatever the string searched.
- `Fields` splits with `RemoveEmptyEntries`, an option .NET Framework accepts, so the `TrimEntries`
  row cannot reach it.
- `Under`, `Find` and `FieldsBy` make the same three calls with a parameter where the others have a
  constant: a path that may hold a character .NET Framework rejects, a string searched for under the
  current culture, and options that may be `TrimEntries`. Each row can reach its call, so each method
  stays Divergent.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Calls.Cache()` | Equivalent | `congruence` |
| `Calls.Slash(string)` | Equivalent | `congruence` |
| `Calls.Fields(string)` | Equivalent | `congruence` |
| `Calls.Under(string)` | Divergent (EQ006, a runtime-changed API) | none |
| `Calls.Find(string, string)` | Divergent (EQ006, a runtime-changed API) | none |
| `Calls.FieldsBy(string, StringSplitOptions)` | Divergent (EQ006, a runtime-changed API) | none |

Exit code: 1 (a new Divergent result).
