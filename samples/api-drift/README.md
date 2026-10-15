# api-drift

Identical source text binds different members on each side (ADR 0020, ticket M3-009):

- `s.Split(',')` binds `String.Split(params char[])` on .NET Framework 4.8 and
  `String.Split(char, StringSplitOptions)` on .NET 10.
- `s.Contains('x')` binds `Enumerable.Contains<char>` on .NET Framework 4.8 (the legacy side has
  `using System.Linq;`) and `String.Contains(char)` on .NET 10.

The shipped API-equivalence catalogue rewrites each legacy call to its modern member, and each result
lists the entry applied in `properties.equivalencesApplied`. A rewritten call is closed exactly when
a direct call of its modern member is (ADR 0041, ticket P2-184), so both `Split` calls and both
`Contains` calls are closed, and the replay taints what the solver chooses for them (ADR 0056).

- `Tally.Count` reads a field and calls a member after the `Split`. Neither side's closed call
  writes the heap, so `Count` is Equivalent.
- `Tally.Total` adds one more on the modern side after the `Split`. The difference shows only past
  a closed call that can throw, so `Total` is Unknown (`abstraction`), naming `String.Split`.
- `HasX` differs on a null `s`, which is the real behaviour change: the legacy call throws
  `ArgumentNullException`, the modern one `NullReferenceException`. The legacy side reaches the
  closed `String.Contains(char)` with a null receiver, so `HasX` is Unknown (`abstraction`) with
  `agreesWhen` `s != null` (ticket P2-202 owns giving it back its Divergent).

## Expected verdicts

| Procedure | Verdict | `equivalencesApplied` |
|---|---|---|
| `Text.Parts(string)` | Equivalent | `bcl.string-split-one-char` |
| `Text.HasX(string)` | Unknown (`abstraction`; equivalent when `s != null`) | `bcl.string-contains-char` |
| `Tally.Count(string)` | Equivalent | `bcl.string-split-one-char` |
| `Tally.Total(string)` | Unknown (`abstraction`) | `bcl.string-split-one-char` |
| `Tally.Add(int)` | Equivalent | none |

Exit code: 0 (Unknown does not fail the run without `--fail-on unknown`).
