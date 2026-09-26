# renamed-locals

Same logic as `identical`, but the modern side renames every local variable
(`sum` -> `total`, `result` -> `best`). Local names never survive lowering (SSA renames
everything anyway), so this must verify exactly like `identical`.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Calculator.Add(int, int)` | Equivalent |
| `Calculator.Max(int, int)` | Equivalent |
| `Calculator.SumTo(int)` | Equivalent — unbounded, by lockstep induction (rung 2); `sum`/`i` renamed to `total`/`k` |

Exit code: 0 (no Divergent or Unknown result).
