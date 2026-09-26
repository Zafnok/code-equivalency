# removed-null-check

Legacy guards `name` with an explicit null check and throws `ArgumentNullException`.
Modern drops the guard; the same null input now flows into `name.ToUpper()` and throws
`NullReferenceException` instead. Per VERIFICATION-MODEL.md section 1, the exception
*type* is observable — the message is not — so this is a divergence even though both
sides throw.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Greeter.Greet(string)` | Divergent — counterexample `name = null` (legacy throws `ArgumentNullException`, modern throws `NullReferenceException`) |

Exit code: 1 (a new Divergent result).
