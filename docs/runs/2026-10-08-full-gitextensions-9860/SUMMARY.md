# full run: gitextensions-9860

- Pair: human, gitextensions/gitextensions PR #9860 (net5.0 to net6.0, a bump with fixes), legacy bcd0c2617bdd, modern 37797ea4dd74
- Corpus list: `tools/corpus/pairs.csv`
- Migrated by: human (upstream PR #9860)
- equiv: 18b751f7, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 317s (5m17s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 unverified procedures, 0 tool execution notifications. The box ran nothing else. The last summary of this pair is `docs/runs/2026-10-03-full-gitextensions-9860/SUMMARY.md`; `docs/runs/2026-10-08-scoreboard-rerun.md` sets the two side by side.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 43 | 0.001 | +0.001 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.648 | n/a |
| match | 1 | 0.034 | n/a |
| lower | 14073 | 69.384 | +33.074 |
| verify | 14073 | 212.767 | +2.680 |
| write | 1 | 0.254 | +0.000 |

## Load
- Projects: legacy 43 with a detected runtime, modern 43; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy Setup; modern Setup
- Runtimes detected (`run.properties.runtimes`): legacy net5.0 (43); modern net6.0 (43)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14075 | 14075 |
| analysed lines | 189310 | 189332 |

- Matched pairs 14073; without opaque 10586 (75.2%); whole-body opaque 126 (0.9%); congruent 13473 (95.7%)
- Unchanged share: 95.7% (`pairsCongruent` / `matchedPairs`)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 95.7%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). Owning tickets were not looked up again for this rerun.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| switch-pattern | 1189 | 1189 | n/a |
| Binary | 654 | 654 | n/a |
| Conversion | 563 | 563 | n/a |
| InstanceReference | 426 | 426 | n/a |
| DefaultValue | 359 | 359 | n/a |
| DelegateCreation | 212 | 216 | n/a |
| LocalFunction | 212 | 212 | n/a |
| CaughtException | 124 | 124 | n/a |
| InterpolatedString | 87 | 80 | n/a |
| DeconstructionAssignment | 77 | 77 | n/a |
| iterator | 73 | 73 | n/a |
| ArrayElementReference | 60 | 60 | n/a |
| CompoundAssignment | 59 | 59 | n/a |
| no-body | 53 | 53 | n/a |
| Tuple | 48 | 48 | n/a |

## Changed code
- Changed pairs 600 of 14073; without opaque 206; whole-body opaque 60
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 34.3%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 206 | n/a |
| "no-body" | 53 | n/a |
| "switch-pattern" | 36 | n/a |
| "InterpolatedString" | 30 | n/a |
| "DelegateCreation" | 29 | n/a |
| "rebound-call" | 25 | n/a |
| "DefaultValue" | 22 | n/a |
| "LocalFunction" | 16 | n/a |
| "Conversion" | 13 | n/a |
| "InterpolatedString+switch-pattern" | 12 | n/a |
| "Binary" | 9 | n/a |
| "CaughtException" | 8 | n/a |
| "iterator" | 7 | n/a |
| "LocalFunction+switch-pattern" | 7 | n/a |
| "DefaultValue+InterpolatedString" | 6 | n/a |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 60 | 60 |
| distinct members | 16 | 16 |
| pairs with any | 32 | 32 |

- Package changes: n/a (`-Packages` was not run for this rerun)

## Verdicts (full and seeded only)
- By rule: EQ001 13734, EQ002 8, EQ003 344, EQ004 2, EQ005 2, EQ006 7
- By proofMethod, Equivalent results: congruence 13473, bounded 246, lockstep-induction 15
- Changed pairs by outcome: proved Equivalent 261 (43.5%), Unknown 324 (54.0%; 344 less 20 `unmatched-overload`), Divergent 15 (2.5%; EQ002 8, EQ006 7)
- Unknown by scope: line 179, method 165. Line-scoped Unknown share: 52.0%
- Top Unknown reasons: opaque 248, timeout 48, abstraction 26, unmatched-overload 20, recursion 2
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 87, wallClock 0
- `unbound` Unknowns: 0
- Top abstractions: delegate 57, opaque switch-pattern 8, opaque DefaultValue 4, opaque Binary 2, `op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder)` 2, opaque Conversion 2, `get:System.String::get_Length()` 2, opaque Tuple 2, opaque ref-argument 2
- Review list: 53 groups for 359 flagged results (EQ002 + EQ003 + EQ006); flagged results as a share of matched pairs: 2.6%. Top five: `EQ003 opaque:InterpolatedString`: 54, `EQ003 opaque:no-body`: 53, `EQ003 timeout`: 48, `EQ003 opaque:DelegateCreation`: 27, `EQ003 opaque:rebound-call`: 25
- No EQ002 or EQ006 was adjudicated in this rerun. Divergent precision rests on the earlier adjudications until tickets P2-124 and P2-130 run theirs.

## Tests (full only)
- Not run for this rerun. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a
