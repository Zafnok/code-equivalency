# generic-enum-members

A version upgrade, .NET 8 to .NET 10, that takes the analyzer's suggestion to call the generic
`Enum` members (CA2263; ADR 0020, ticket P2-117):

- `Enum.IsDefined(typeof(DayOfWeek), d)` becomes `Enum.IsDefined(d)`;
- `Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>()` becomes `Enum.GetValues<DayOfWeek>()`.

Nothing changes: the `Type`-taking member with a `typeof` of an enum does what the generic member
constructed with that enum does. The catalogue has an entry for each, whose adapter takes the
generic member's type argument from the `typeof` (`{"typeArgument": 0}`), so the legacy call is
rewritten to the modern one. `Enum.GetValues(Type)` returns the `DayOfWeek[]` typed as `Array`, and
`Cast<DayOfWeek>()` returns the `DayOfWeek[]` it is given, so the legacy `foreach` enumerates the
same array the modern one indexes, and it is lowered as that index loop.

## Expected verdicts

| Procedure | Verdict | `equivalencesApplied` |
|---|---|---|
| `Days.Known(DayOfWeek)` | Equivalent | `bcl.enum-is-defined-generic` |
| `Days.Count()` | Equivalent | `bcl.enum-get-values-generic` |

Exit code: 0.
