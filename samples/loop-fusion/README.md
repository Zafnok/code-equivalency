# loop-fusion

Legacy makes two passes over `0..count-1`: the first counts the `i` inside `[low, high)`, the
second counts every `i`, and it returns the difference. Modern fuses the two passes into one
loop. Two loops against one cannot be aligned, so rungs 2 and 3 of the loop ladder do not apply
(VERIFICATION-MODEL.md section 5.1).

Rung 4 hands the pair's Horn clauses to Z3 Spacer, which schedules the legacy second loop
alone after the modern loop has returned and finds a coupling invariant over the integers. No
operation of either side can overflow (the counters stay below `count`), which a second Spacer
query proves, so the proof over the integers holds for the bitvectors (`chcMode: int`).

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Window.CountOutside(int, int, int)` | Equivalent — `proofMethod: chc`, `chcMode: int`, with Spacer's invariant in `properties.invariant` (rung 4) |

Exit code: 0.
