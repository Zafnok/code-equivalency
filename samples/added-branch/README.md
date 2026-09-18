# added-branch

Modern adds a special case for `x == 0` that legacy does not have. Every other input is
unaffected, so this is the smallest possible counterexample-bearing sample.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Doubler.Double(int)` | Divergent — counterexample `x = 0` (legacy returns `0`, modern returns `-1`) |
