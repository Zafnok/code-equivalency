# added-null-guard

The shape of Git Extensions' "no functional change" pull request: the modern side adds
`?? string.Empty`. Legacy `Greet` dereferences `name` and throws `NullReferenceException` on
`null`; modern `Greet` returns a greeting for the empty string. The pair is Divergent, and the
only inputs it differs on are those the legacy side throws on (ADR 0037, ticket P1-020).

`Shout` goes the other way. Legacy returns `"HELLO"` for `null`; modern dropped that branch
and throws.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Greeter.Greet(string)` | Divergent, counterexample `name = null` (legacy throws `NullReferenceException`, modern returns). `divergenceKind` is `removed-failures-only`: `removedFailures` is `found`, `newFailures` and `resultChanges` are `none-proved`. The message ends with `The modern side differs only where the legacy side throws.` |
| `Greeter.Shout(string)` | Divergent, counterexample `name = null` (legacy returns, modern throws `NullReferenceException`). `divergenceKind` is `new-failures-only` |

Exit code: 1 (new Divergent results).
