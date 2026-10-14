# full run: openra-17989

- Pair: human, OpenRA/OpenRA PR #17989 (Mono/net472 plus .NET 5 on Windows), legacy fef7a018f2b2, modern 5e74e58b2204
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream PR #17989)
- equiv: e98fd1b9, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 292s (4m52s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: no `run.properties.unverified`, 0 tool execution notifications. The box ran nothing else; it ran straight after the `duplicati-3124` run, on the same build. The last summary of this pair is `docs/runs/2026-10-01-full-openra-17989/SUMMARY.md` (equiv ef79ff6), whose `full` run crashed before the first verdict (P2-082) and whose numbers come from a `--lower-only` census. This is the pair's first run to a result.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 9 | 0.001 | +0.000 |
| load-modern | 8 | 0.000 | +0.000 |
| enumerate | 2 | 0.516 | n/a |
| match | 1 | 0.022 | n/a |
| lower | 10120 | 72.982 | -3.218 |
| verify | 10120 | 205.129 | -41.581 |
| write | 1 | 0.217 | +0.000 |

## Load
- Projects: legacy 9 with a detected runtime, modern 8; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy OpenRA.WindowsLauncher; modern OpenRA.Test, OpenRA.WindowsLauncher
- Runtimes detected (`run.properties.runtimes`): legacy net472 (9); modern net5.0 (4), netstandard2.1 (4)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 10182 | 10151 |
| analysed lines | 131090 | 130228 |

- Matched pairs 10120; without opaque 7893 (78.0%); whole-body opaque 338 (3.3%); congruent 9799 (96.8%)
- Unchanged share: 96.8% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 1306 of 1309 legacy `.cs` files (177511 of 179418 lines, 98.9%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 96.8%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| InstanceReference | 563 | 563 | none |
| iterator | 332 | 332 | none |
| Layout | 309 | 309 | none |
| Binary | 280 | 280 | none open (P2-087 done) |
| ArrayElementReference | 245 | 245 | none |
| Conversion | 215 | 215 | none open (P2-123 done) |
| DelegateCreation | 185 | 185 | none open (P2-136 done) |
| DefaultValue | 87 | 87 | none open (P2-138 done) |
| ArrayCreation | 75 | 75 | none |
| CaughtException | 62 | 62 | P2-180 (new; P2-140 done) |
| CompoundAssignment | 55 | 55 | none open (P2-141 done) |
| Tuple | 44 | 44 | none |
| ref-argument | 38 | 38 | none |
| None | 24 | 24 | none |
| IsType | 21 | 21 | none |

## Changed code
- Changed pairs 321 of 10120; without opaque 179; whole-body opaque 16
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 55.8%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 179 | n/a |
| "DelegateCreation" | 31 | none open (P2-136 done) |
| "ArrayElementReference" | 19 | none |
| "CaughtException" | 14 | P2-180 |
| "Binary" | 10 | none open (P2-087 done) |
| "iterator" | 10 | none |
| "no-body" | 6 | P2-165 |
| "Layout" | 5 | none |
| "Conversion" | 4 | none open (P2-123 done) |
| "Tuple" | 3 | none |
| "ArrayCreation+ArrayElementReference+Conversion" | 2 | none; none; none open (P2-123 done) |
| "ArrayElementReference+InstanceReference+Tuple" | 2 | none |
| "Binary+Layout" | 2 | none open (P2-087 done); none |
| "CompoundAssignment" | 2 | none open (P2-141 done) |
| "DelegateCreation+Layout" | 2 | none open (P2-136 done); none |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 358 | 357 |
| distinct members | 58 | 58 |
| pairs with any | 188 | 187 |

- Package changes: 0 version changed, 10 legacy only, 23 modern only

## Verdicts (full and seeded only)
- By rule: EQ001 9803, EQ002 2, EQ003 267, EQ004 31, EQ005 62, EQ006 48
- By proofMethod, Equivalent results: congruence 9799, bounded 3, lockstep-induction 1
- Changed pairs by outcome: proved Equivalent by the solver 4 (1.2%), Unknown 267 (83.2%), Divergent 50 (15.6%; EQ002 2, EQ006 48)
- Unknown by scope: line 15, method 252. Line-scoped Unknown share: 5.6%
- Top Unknown reasons: timeout 114, abstraction 99, opaque 42, unaligned-loop 10, recursion 2
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 232, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 242, callee 63, no-fingerprint 6, operation 5, type-layout 4, symbol 1. Of the 242 `runtime-rule` pairs, whose two texts are equal, none is proved, 47 are EQ006 and 195 Unknown (timeout 85, abstraction 81, opaque 22, unaligned-loop 7).
- `unbound` Unknowns: 0
- Top abstractions: delegate 43, `conv.f32.i32` 19, `f32.mul` 17, `System.String::Concat(string,string)` 16, `conv.i32.f32` 15, `System.String::Substring(int,int)` 12, `f32.div` 11, `System.String::Substring(int)` 9, `f32.sub` 8, `System.String::IsNullOrEmpty(string)` 6, opaque ArrayElementReference 5, `conv.f32.f64` 4, `conv.f64.i32` 4, `get:System.String::get_Length()` 4, `f32.add` 4
- Review list: 46 groups for 317 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 3.1%. Top five: `EQ003 timeout`: 114, `EQ003 abstraction`: 86, `EQ003 opaque:iterator`: 10, `EQ003 unaligned-loop`: 10, `EQ003 opaque:ArrayElementReference`: 8
- Divergent adjudication: `docs/runs/2026-10-14-migrations-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- First run to a result: no crash (P2-082, P2-083 and P2-125 are done), no skipped project, no `unbound` Unknown.
- Unedited bodies a runtime rule keeps out of congruence: 242 of 321 changed pairs, none proved: P2-181 (new). The solver proves 4 of 321 changed pairs.
- `DelegateCreation` alone in 31 of 321 changed pairs (9.7%): P2-182 (new); `ArrayElementReference` alone in 19 (5.9%): P2-183 (new).
- The four unhosted `netstandard2.1` mod projects are judged against every runtime the table covers (net40 to net10.0); 17 of the 48 EQ006 are in them: P2-191 (new).
- Divergent sample (32 of 50): 0 confirmed, 13 false positive, 19 undetermined; precision 0% (0 of 13). False-positive causes: P2-184, P2-188, P2-189, P2-190, P2-191 (new).
