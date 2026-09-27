# loop-to-linq

Legacy counts the positive elements of an array with a `for` loop. Modern writes out the loop
`values.Count(v => v > 0)` runs: an enumerator's index that starts at `-1` and is
pre-incremented before each bounds check (the call itself would be a callee the verifier cannot
see into). The two loops run in step, but their counters start apart (`i` from `0`, `index`
from `-1`) and modern increments before its test, so the loop ladder's rung 2 fails its base
obligation (the paired header states start unequal) and rung 3 does not apply
(VERIFICATION-MODEL.md section 5.1).

Rung 4 hands the pair's Horn clauses to Z3 Spacer, which finds the coupling invariant
`old.i = new.index + 1` with equal counts over the integers. That invariant also holds with
wrap-around arithmetic, so it proves the pair over the bitvectors (`chcMode: bitvector`), with
no proof needed that the count cannot overflow.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Readings.CountPositive(int[])` | Equivalent — `proofMethod: chc`, `chcMode: bitvector`, with Spacer's invariant in `properties.invariant` (rung 4) |

Exit code: 0.
