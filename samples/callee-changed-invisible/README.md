# callee-changed-invisible

A caller nobody touched, over a callee that changed in a way the caller cannot see (ADR 0036
decision 2, ticket P1-010). `Classify(int a) => Score(a) > 0 ? "pos" : "neg"` is identical on both
sides. `Score` returns `a` on the legacy side. On the modern side it returns `a - 1` when `a > 10`,
so it changes only for inputs whose score was already above 10, and those scores stay positive.

`Score` is Divergent (at `a = 11` it returns 11 against 10). `Classify` uses only `Score(a) > 0`, so
the run admits the caller-sufficient contract "`Score` throws on both sides or neither, with the same
exception type, makes the same calls, and `r > 0` has the same value on both sides" for the `Score`
pair. It proves that contract on `Score`'s own product, then proves `Classify` again with each side's
call to `Score` given its own result, related to the other only by the contract. `Classify` is
Equivalent with `proofMethod: bounded+contract`. The contract is listed in
`properties.contractsUsed`, and `unprovenAssumptions` is empty. Compare `callee-changed`, whose caller
returns the changed value, so no contract can hide the change there.

## Expected verdicts

| Procedure | Verdict | `proofMethod` | `assumedCallees` | `unprovenAssumptions` |
|---|---|---|---|---|
| `Grading.Score(int)` | Divergent | | | |
| `Grading.Classify(int)` | Equivalent | `bounded+contract` | `Score` | (none) |

Exit code: 1 (a new Divergent result).
