# switch-expression-no-match

The same `switch` expressions on .NET Framework 4.8 and on .NET 10 (ADR 0024 as clarified by ticket
P2-144). `Codes.cs` is the same text on both sides.

A `switch` expression that matches no arm throws, and the source does not write the throw. The
compiler throws `System.Runtime.CompilerServices.SwitchExpressionException` where the reference
assemblies have the type (.NET Core 3.0 and later) and `System.InvalidOperationException` where
they do not (.NET Framework). The exception type is an observable, so on an input no arm matches
the two sides differ, and `runtime-changes.json` has a row for it.

- `Weight` has arms for 1 and 2 only. Any other `code` reaches the throw: the pair is Divergent
  with rule EQ006, and the message cites the row.
- `WeightOrZero` ends in a discard arm, which always matches. The compiler emits no throw for it,
  and neither the bound fingerprint nor the lowered body holds one.
- `Sign` has no discard arm, but `true` and `false` are every value of `bool`, so it has no throw
  either.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Codes.Weight(int)` | Divergent (EQ006, a runtime-changed API) | none |
| `Codes.WeightOrZero(int)` | Equivalent | `congruence` |
| `Codes.Sign(bool)` | Equivalent | `congruence` |

Exit code: 1 (a new Divergent result).
