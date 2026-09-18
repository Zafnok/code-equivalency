# renamed-locals

Same logic as `identical`, but the modern side renames every local variable
(`sum` -> `total`, `result` -> `best`). Local names never survive lowering (SSA renames
everything anyway), so this must verify exactly like `identical`.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Calculator.Add(int, int)` | Equivalent |
| `Calculator.Max(int, int)` | Equivalent |
