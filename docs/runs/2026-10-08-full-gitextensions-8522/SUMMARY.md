# full run: gitextensions-8522

- Pair: human, gitextensions/gitextensions PR #8522 (.NET Framework 4.8 to .NET 5), legacy 3f4ed21998af, modern 5190ba5c1a5f
- Corpus list: `tools/corpus/pairs.csv`
- Migrated by: human (upstream PR #8522)
- equiv: 18b751f7, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 364s (6m04s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 unverified procedures, 0 tool execution notifications. The box ran nothing else. The last summary of this pair is `docs/runs/2026-09-30-full-gitextensions-8522/SUMMARY.md`; `docs/runs/2026-10-08-scoreboard-rerun.md` sets the two side by side.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 48 | 0.001 | +0.001 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.540 | n/a |
| match | 1 | 0.024 | n/a |
| lower | 13592 | 80.164 | +40.730 |
| verify | 13592 | 244.525 | -24.591 |
| write | 1 | 0.239 | +0.000 |

## Load
- Projects: legacy 48 with a detected runtime, modern 43; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy Setup; modern Setup
- Runtimes detected (`run.properties.runtimes`): legacy net461 (48); modern net5.0 (43)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 13784 | 13607 |
| analysed lines | 186633 | 184691 |

- Matched pairs 13592; without opaque 10171 (74.8%); whole-body opaque 124 (0.9%); congruent 12682 (93.3%)
- Unchanged share: 93.3% (`pairsCongruent` / `matchedPairs`)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 93.3%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). Owning tickets were not looked up again for this rerun.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| switch-pattern | 1199 | 1196 | n/a |
| Binary | 652 | 652 | n/a |
| Conversion | 542 | 541 | n/a |
| InstanceReference | 374 | 374 | n/a |
| DefaultValue | 316 | 316 | n/a |
| DelegateCreation | 209 | 206 | n/a |
| LocalFunction | 202 | 202 | n/a |
| rebound-call | 131 | 131 | n/a |
| CaughtException | 125 | 124 | n/a |
| DeconstructionAssignment | 80 | 80 | n/a |
| InterpolatedString | 74 | 74 | n/a |
| iterator | 73 | 73 | n/a |
| ArrayElementReference | 62 | 62 | n/a |
| CompoundAssignment | 58 | 58 | n/a |
| no-body | 51 | 51 | n/a |

## Changed code
- Changed pairs 910 of 13592; without opaque 377; whole-body opaque 60
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 41.4%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 377 | n/a |
| "rebound-call" | 107 | n/a |
| "switch-pattern" | 67 | n/a |
| "no-body" | 51 | n/a |
| "LocalFunction" | 29 | n/a |
| "Binary" | 25 | n/a |
| "DelegateCreation" | 21 | n/a |
| "DefaultValue" | 15 | n/a |
| "CaughtException" | 14 | n/a |
| "LocalFunction+switch-pattern" | 10 | n/a |
| "Binary+switch-pattern" | 9 | n/a |
| "iterator" | 9 | n/a |
| "rebound-call+switch-pattern" | 8 | n/a |
| "CompoundAssignment" | 8 | n/a |
| "Conversion" | 8 | n/a |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 906 | 906 |
| distinct members | 99 | 96 |
| pairs with any | 520 | 521 |

- Package changes: n/a (`-Packages` was not run for this rerun)

## Verdicts (full and seeded only)
- By rule: EQ001 12727, EQ002 30, EQ003 615, EQ004 15, EQ005 192, EQ006 239
- By proofMethod, Equivalent results: congruence 12682, bounded 40, lockstep-induction 5
- Changed pairs by outcome: proved Equivalent 45 (4.9%), Unknown 596 (65.5%; 615 less 19 `unmatched-overload`), Divergent 269 (29.6%; EQ002 30, EQ006 239)
- Unknown by scope: line 172, method 443. Line-scoped Unknown share: 28.0%
- Top Unknown reasons: opaque 256, abstraction 218, timeout 109, unmatched-overload 19, unaligned-loop 12, recursion 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 179, wallClock 0
- `unbound` Unknowns: 0
- Top abstractions: delegate 293, opaque switch-pattern 174, `get:System.String::get_Length()` 66, opaque Conversion 40, opaque DefaultValue 37, `op:System.String::op_Equality(string,string)` 31, `op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder)` 28, opaque Binary 26, `op:System.String::op_Inequality(string,string)` 10, `op:GitExtUtils.ArgumentString::op_Implicit(string)` 10
- Review list: 130 groups for 884 flagged results (EQ002 + EQ003 + EQ006); flagged results as a share of matched pairs: 6.5%. Top five: `EQ003 timeout`: 109, `EQ003 abstraction`: 106, `EQ003 opaque:rebound-call`: 99, `EQ003 abstraction:switch-pattern`: 61, `EQ003 opaque:no-body`: 51
- No EQ002 or EQ006 was adjudicated in this rerun. Divergent precision rests on the earlier adjudications until tickets P2-124 and P2-130 run theirs.

## Tests (full only)
- Not run for this rerun. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a
