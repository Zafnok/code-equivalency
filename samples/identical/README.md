# identical

Legacy and modern `Calculator` are byte-for-byte the same logic (modern is just
recompiled against net10.0/SDK-style tooling). Baseline case: nothing should ever be
reported as a difference.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Calculator.Add(int, int)` | Equivalent |
| `Calculator.Max(int, int)` | Equivalent |
