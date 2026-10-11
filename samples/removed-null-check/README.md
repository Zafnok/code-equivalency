# removed-null-check

Legacy guards `name` with an explicit null check and throws `ArgumentNullException`.
Modern drops the guard; the same null input now flows into `name.ToUpper()` and throws
`NullReferenceException` instead. Per VERIFICATION-MODEL.md section 1, the exception
*type* is observable — the message is not — so this is a divergence even though both
sides throw.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Greeter.Greet(string)` | Divergent — counterexample `name = null` (legacy throws `ArgumentNullException`, modern throws `NullReferenceException`); `agreesWhen.text` is `name != null` (ADR 0048): the pair is proved equivalent for every other argument. `divergenceKind` is `undetermined` (ADR 0037, ticket P1-020): both sides throw on `null`, so there is no new failure, no removed one and no changed result, and the three answers are all `none-proved` |

Exit code: 1 (a new Divergent result).
