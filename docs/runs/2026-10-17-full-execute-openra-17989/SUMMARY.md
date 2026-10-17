# full run: openra-17989

- Pair: human, OpenRA/OpenRA PR #17989 (Mono/net472 plus .NET 5), legacy fef7a018f2b2, modern 5e74e58b2204
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: de93b9c7, mode full with `--execute`, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, committed config `tools/corpus/configs/openra-17989.json` (P2-197), wall-clock 272s (4m32s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-237: the three migration pairs one after the other from one build, nothing else started from this session while they ran, with the .NET 5.0.17 runtime installed for these runs. The last summary of this pair is `docs/runs/2026-10-15-full-openra-17989/SUMMARY.md`; `docs/runs/2026-10-17-precision-execute-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 9 | 0.002 | +0.000 |
| load-modern | 8 | 0.000 | +0.000 |
| enumerate | 2 | 0.662 | n/a |
| match | 1 | 0.025 | n/a |
| lower | 10120 | 95.742 | -12.515 |
| verify | 10120 | 158.693 | -53.434 |
| execute | 10120 | 1.260 | +1.250 |
| write | 1 | 0.252 | +0.000 |

## Load
- Projects: legacy 9 with a detected runtime, modern 8; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy OpenRA.WindowsLauncher; modern OpenRA.Test, OpenRA.WindowsLauncher
- Runtimes detected (`run.properties.runtimes`): legacy net472 (9) [source: attribute 9]; modern net5.0 (8) [source: attribute 3, host 1, config 4]
- Execution: legacy on the .NET Framework 4.8 CLR (an `app.config` naming each project's net472 sku), modern on `Microsoft.NETCore.App` 5.0.17 (`rollForward: Disable`). Replay drivers run on `Microsoft.NETCore.App` only, so no `Microsoft.WindowsDesktop.App` 5.0 was needed or installed.

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
| "no-body" | 6 | P2-214 |
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

- Package changes: 0 version changed, 10 legacy only, 23 modern only (unchanged since P2-212)

## Verdicts (full and seeded only)
- By rule: EQ001 9924, EQ002 0, EQ003 162, EQ004 31, EQ005 62, EQ006 34
- By proofMethod, Equivalent results: congruence 9895, bounded 18, lockstep-induction 11
- Changed pairs by outcome: proved Equivalent by the solver 29 (12.9%), Unknown 162 (72.0%), Divergent 34 (15.1%; EQ002 0, EQ006 34). `unmatched-overload` Unknowns, not matched pairs: 0
- Unknown by scope: line 12, method 150. Line-scoped Unknown share: 7.4%
- Top Unknown reasons: timeout 68, abstraction 50, opaque 36, unaligned-loop 6, recursion 2
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 154, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 152, callee 63, operation 5, type-layout 4, symbol 1. Of the 152 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 33 are EQ006, 0 EQ002 and 119 Unknown (timeout 53, abstraction 41, opaque 20, unaligned-loop 5).
- `unbound` Unknowns: 0
- Top abstractions: delegate 141, `System.String::Concat(string,string)` 28, `op:System.Reflection.FieldInfo::op_Equality(System.Reflection.FieldInfo,System.Reflection.FieldInfo)` 6, `op:System.String::op_Equality(string,string)` 5, `System.String::IsNullOrEmpty(string)` 4, `System.String::Split(char,System.StringSplitOptions)` 4, opaque DefaultValue 4, `System.String::Substring(int,int)` 4, `System.String::Substring(int)` 4, `System.String::StartsWith(string,System.StringComparison)` 4, `f32.mul` 4, `conv.i32.f64` 4, `f64.div` 4, opaque Conversion 4, `get:System.String::get_Length()` 3
- Review list: 39 groups for 196 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 1.9%. Top five: `EQ003 timeout`: 68, `EQ003 abstraction`: 44, `EQ003 opaque:iterator`: 8, `EQ003 opaque:ArrayElementReference`: 7, `EQ003 opaque:no-body`: 6
- Replay (`properties.replay`, ADR 0035 decision 2), EQ002 0: none
- Replay (`properties.replay`, ADR 0035 decision 2), EQ006 34: not-constructible 34
- Why a replay is not constructible: the divergence is in the call trace 19, the model constrains a field, cast or array 9, not public 4, a side gave no comparable outcome 1, no public parameterless constructor on the receiver type 1
- Divergent adjudication: `docs/runs/2026-10-17-precision-execute-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Every count equals P2-212's run (`bfbf413e`): EQ006 34, EQ002 0, the solver proves 29 changed pairs.
- Replay: every EQ006 (34) is `not-constructible`. No `not-reproduced`.
- P2-212's sample executed (30 rows, 23 undetermined): 2 confirmed by `probe` (`MiniYaml::FromFile(...)`, `Platform::get_RuntimeVersion()`), 21 still undetermined; precision 22.2% (2 of 9).
- `probe` loads a pair with the default config, so the four mod projects, whose net5.0 comes from the pair's config, have no detected runtime there: 4 sampled rows could not be run: P2-241 (new).
- `DelegateCreation` alone in 20 of 225 changed pairs (8.9%): P2-182; `CaughtException` alone in 14 (6.2%): P2-180.
