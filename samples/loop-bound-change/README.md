# loop-bound-change

Legacy sums `0..n-1`; modern's loop guard is off by one (`<=` instead of `<`) and sums
`0..n`. The two loops align at the same CFG position, so per VERIFICATION-MODEL.md
section 5.1 this is exactly the shape rung 1 (bounded unrolling) is meant to catch: the
smallest counterexample is `n = 1` (legacy returns `0`, modern returns `1`), well within
a bound of `k = 2` iterations.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Summation.SumUpTo(int)` | Divergent — counterexample `n = 1`, provable at bounded unrolling `k = 2` (rung 1) |

Exit code: 1 (a new Divergent result).
