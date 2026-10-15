# full run: openra-17989

- Pair: human, OpenRA/OpenRA PR #17989 (Mono/net472 plus .NET 5 on Windows), legacy fef7a018f2b2, modern 5e74e58b2204
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: bfbf413e, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 217s (3m37s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-212: the five large pairs one after the other from one build, on a box that ran nothing else but a file-reading trace agent of the adjudication. The last summary of this pair is `docs/runs/2026-10-14-full-openra-17989/SUMMARY.md`; `docs/runs/2026-10-15-precision-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 9 | 0.001 | +0.000 |
| load-modern | 8 | 0.000 | +0.000 |
| enumerate | 2 | 0.518 | n/a |
| match | 1 | 0.019 | n/a |
| lower | 10120 | 67.162 | +2.753 |
| verify | 10120 | 136.578 | -55.930 |
| write | 1 | 0.232 | +0.000 |

## Load
- Projects: legacy 9 with a detected runtime, modern 8; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy OpenRA.WindowsLauncher; modern OpenRA.Test, OpenRA.WindowsLauncher
- Runtimes detected (`run.properties.runtimes`): legacy net472 (9) [source: attribute 9]; modern net5.0 (8) [source: attribute 3, config 4, host 1]

## Census
| | legacy | modern |
|---|---|---|
| procedures | 10182 | 10151 |
| analysed lines | 131090 | 130228 |

- Matched pairs 10120; without opaque 7889 (78.0%); whole-body opaque 338 (3.3%); congruent 9895 (97.8%)
- Unchanged share: 97.8% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 1306 of 1309 legacy `.cs` files (177511 of 179418 lines, 98.9%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 97.8%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| InstanceReference | 563 | 563 | none |
| iterator | 332 | 332 | none |
| Layout | 316 | 316 | none |
| Binary | 280 | 280 | none open (P2-087 done) |
| ArrayElementReference | 245 | 245 | P2-183 (OpenRA forms) |
| Conversion | 215 | 215 | none open (P2-123 done) |
| DelegateCreation | 185 | 185 | P2-182 (OpenRA forms; P2-136 done) |
| DefaultValue | 87 | 87 | none open (P2-138 done) |
| ArrayCreation | 75 | 75 | none |
| CaughtException | 62 | 62 | P2-180 |
| CompoundAssignment | 55 | 55 | none open (P2-141 done) |
| Tuple | 43 | 43 | none |
| ref-argument | 38 | 38 | none |
| None | 24 | 24 | none |
| IsType | 21 | 21 | none |

## Changed code
- Changed pairs 225 of 10120; without opaque 119; whole-body opaque 14
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 52.9%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 119 | n/a |
| "DelegateCreation" | 20 | P2-182 (OpenRA forms; P2-136 done) |
| "CaughtException" | 14 | P2-180 |
| "ArrayElementReference" | 9 | P2-183 (OpenRA forms) |
| "iterator" | 8 | none |
| "no-body" | 6 | none open (P2-165 done) |
| "Binary" | 5 | none open (P2-087 done) |
| "Layout" | 5 | none |
| "Conversion" | 4 | none open (P2-123 done) |
| "ArrayCreation+ArrayElementReference+Conversion" | 2 | none; P2-183 (OpenRA forms); none open (P2-123 done) |
| "ArrayElementReference+InstanceReference+Tuple" | 2 | P2-183 (OpenRA forms); none; none |
| "Binary+Layout" | 2 | none open (P2-087 done); none |
| "CompoundAssignment" | 2 | none open (P2-141 done) |
| "Tuple" | 2 | none |
| "ref-argument" | 2 | none |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 329 | 328 |
| distinct members | 47 | 47 |
| pairs with any | 172 | 171 |

- Package changes: 0 version changed, 10 legacy only, 23 modern only

## Verdicts (full and seeded only)
- By rule: EQ001 9924, EQ002 0, EQ003 162, EQ004 31, EQ005 62, EQ006 34
- By proofMethod, Equivalent results: congruence 9895, bounded 18, lockstep-induction 11
- Changed pairs by outcome: proved Equivalent by the solver 29 (12.9%), Unknown 162 (72.0%), Divergent 34 (15.1%; EQ002 0, EQ006 34)
- Unknown by scope: line 12, method 150. Line-scoped Unknown share: 7.4%
- Top Unknown reasons: timeout 68, abstraction 50, opaque 36, unaligned-loop 6, recursion 2
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 154, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 152, callee 63, operation 5, type-layout 4, symbol 1. Of the 152 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 33 are EQ006, 0 EQ002 and 119 Unknown (timeout 53, abstraction 41, opaque 20, unaligned-loop 5).
- `unbound` Unknowns: 0
- Top abstractions: delegate 141, `System.String::Concat(string,string)` 28, `op:System.String::op_Equality(string,string)` 5, `System.String::IsNullOrEmpty(string)` 4, `System.String::Split(char,System.StringSplitOptions)` 4, opaque DefaultValue 4, `System.String::Substring(int,int)` 4, `System.String::Substring(int)` 4, `System.String::StartsWith(string,System.StringComparison)` 4, `f32.mul` 4, `conv.i32.f64` 4, `f64.div` 4, opaque Conversion 4, `get:System.String::get_Length()` 3, `System.String::ToUpperInvariant()` 2
- Review list: 39 groups for 196 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 1.9%. Top five: `EQ003 timeout`: 68, `EQ003 abstraction`: 44, `EQ003 opaque:iterator`: 8, `EQ003 opaque:ArrayElementReference`: 7, `EQ003 opaque:no-body`: 6
- Divergent adjudication: `docs/runs/2026-10-15-precision-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown. P2-197's committed config gives the four mod projects net5.0: changed pairs 321 to 225, and the solver proves 29, from 4.
- EQ006 48 to 34, EQ002 2 to 0.
- Divergent sample (30 of 34): 0 confirmed, 7 false positive, 23 undetermined; precision 0% (0 of 7). False-positive causes: P2-219 and P2-220 (new), P2-207, P2-208.
- `DelegateCreation` alone in 20 of 225 changed pairs (8.9%): P2-182; `CaughtException` alone in 14 (6.2%): P2-180.
