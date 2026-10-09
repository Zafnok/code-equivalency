# full run: jellyfin-13023

- Pair: human, jellyfin/jellyfin PR #13023 (net8.0 to net9.0, a pure bump), legacy 5e8c0fe40c0e, modern ceb850c77052
- Corpus list: `tools/corpus/pairs.csv`
- Migrated by: human (upstream PR #13023)
- equiv: 18b751f7, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 181s (3m01s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 unverified procedures, 0 tool execution notifications. The box ran nothing else. The last summary of this pair is `docs/runs/2026-10-03-full-jellyfin-13023/SUMMARY.md`; `docs/runs/2026-10-08-scoreboard-rerun.md` sets the two side by side.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 37 | 0.001 | +0.000 |
| load-modern | 37 | 0.000 | +0.000 |
| enumerate | 2 | 0.600 | n/a |
| match | 1 | 0.020 | n/a |
| lower | 14533 | 91.414 | -38.740 |
| verify | 14533 | 54.054 | +4.296 |
| write | 1 | 0.270 | +0.000 |

## Load
- Projects: legacy 37 with a detected runtime, modern 37; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy none; modern none
- Runtimes detected (`run.properties.runtimes`): legacy net8.0 (37); modern net9.0 (37)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14534 | 14533 |
| analysed lines | 156095 | 156036 |

- Matched pairs 14533; without opaque 12055 (82.9%); whole-body opaque 96 (0.7%); congruent 14370 (98.9%)
- Unchanged share: 98.9% (`pairsCongruent` / `matchedPairs`)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 98.9%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). Owning tickets were not looked up again for this rerun.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| switch-pattern | 1023 | 1023 | n/a |
| Conversion | 919 | 919 | n/a |
| Binary | 505 | 505 | n/a |
| DefaultValue | 302 | 302 | n/a |
| CaughtException | 239 | 239 | n/a |
| CompoundAssignment | 95 | 95 | n/a |
| ArrayCreation | 71 | 71 | n/a |
| call-throw-in-try | 70 | 70 | n/a |
| DelegateCreation | 67 | 67 | n/a |
| iterator | 65 | 65 | n/a |
| ArrayElementReference | 54 | 54 | n/a |
| Tuple | 43 | 43 | n/a |
| DeconstructionAssignment | 39 | 39 | n/a |
| ImplicitIndexerReference | 39 | 39 | n/a |
| CollectionExpression | 37 | 37 | n/a |

## Changed code
- Changed pairs 163 of 14533; without opaque 38; whole-body opaque 34
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 23.3%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 38 | n/a |
| "no-body" | 31 | n/a |
| "Conversion" | 20 | n/a |
| "switch-pattern" | 12 | n/a |
| "Binary" | 6 | n/a |
| "Conversion+switch-pattern" | 5 | n/a |
| "Binary+switch-pattern" | 3 | n/a |
| "CaughtException" | 3 | n/a |
| "iterator" | 3 | n/a |
| "Tuple" | 2 | n/a |
| "CompoundAssignment+Conversion+DefaultValue+switch-pattern" | 2 | n/a |
| "rebound-call+switch-pattern" | 2 | n/a |
| "ArrayCreation+ArrayElementReference" | 2 | n/a |
| "CompoundAssignment" | 2 | n/a |
| "Binary+Conversion+switch-pattern" | 2 | n/a |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 7 | 7 |
| distinct members | 1 | 1 |
| pairs with any | 3 | 3 |

- Package changes: n/a (`-Packages` was not run for this rerun)

## Verdicts (full and seeded only)
- By rule: EQ001 14413, EQ002 10, EQ003 115, EQ004 0, EQ005 1, EQ006 0
- By proofMethod, Equivalent results: congruence 14370, bounded 33, lockstep-induction 10
- Changed pairs by outcome: proved Equivalent 43 (26.4%), Unknown 110 (67.5%; 115 less 5 `unmatched-overload`), Divergent 10 (6.1%; EQ002 10, EQ006 0)
- Unknown by scope: line 31, method 84. Line-scoped Unknown share: 27.0%
- Top Unknown reasons: opaque 65, timeout 23, abstraction 12, unaligned-loop 10, unmatched-overload 5
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 49, wallClock 0
- `unbound` Unknowns: 0
- Top abstractions: delegate 12, `conv.f64.i32` 4, `op:System.String::op_Implicit(string)` 4, opaque Tuple 2, `conv.i32.f32` 2, `f32.mul` 2, `f32.div` 2, `conv.f32.i32` 2, opaque None 2, `op:System.Span`1::op_Implicit(System.Span<byte>)<byte>` 2
- Review list: 30 groups for 125 flagged results (EQ002 + EQ003 + EQ006); flagged results as a share of matched pairs: 0.9%. Top five: `EQ003 opaque:no-body`: 31, `EQ003 timeout`: 23, `EQ003 opaque:Conversion`: 17, `EQ003 unaligned-loop`: 10, `EQ003 abstraction`: 9
- No EQ002 or EQ006 was adjudicated in this rerun. Divergent precision rests on the earlier adjudications until tickets P2-124 and P2-130 run theirs.

## Tests (full only)
- Not run for this rerun. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a
