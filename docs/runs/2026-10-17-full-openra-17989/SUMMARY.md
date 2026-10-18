# full run: openra-17989

- Pair: human, OpenRA/OpenRA PR #17989 (Mono/net472 plus .NET 5), legacy fef7a018f2b2, modern 5e74e58b2204
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: b0f56ff8, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, committed config `tools/corpus/configs/openra-17989.json`, wall-clock 261s (4m21s), exit 1. Exit 1 means Divergent results exist (here a witnessed runtime exposure counts as one, ADR 0060 decision 5). No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-238: the five large pairs one after the other from one Release build, on the checkouts P2-212 fetched and restored, nothing else started from this session while they ran. The last summary of this pair is `docs/runs/2026-10-17-full-execute-openra-17989/SUMMARY.md`; `docs/runs/2026-10-17-runtime-exposure-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 9 | 0.001 | +0.000 |
| load-modern | 8 | 0.000 | +0.000 |
| enumerate | 2 | 0.536 | n/a |
| match | 1 | 0.017 | n/a |
| lower | 10120 | 78.517 | -7.031 |
| verify | 10120 | 168.156 | -80.921 |
| write | 1 | 0.220 | +0.000 |

## Load
- Projects: legacy 9 with a detected runtime, modern 8; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Runtimes detected (`run.properties.runtimes`): legacy net472 (9) [source: attribute 9]; modern net5.0 (8) [source: attribute 3, host 1, config 4]

## Census
| | legacy | modern |
|---|---|---|
| procedures | 10182 | 10151 |
| analysed lines | 131090 | 130228 |

- Matched pairs 10120; without opaque 7953 (78.6%); whole-body opaque 338 (3.3%); congruent 9895 (97.8%)
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
| DelegateCreation | 101 | 97 | none open (P2-182 done) |
| DefaultValue | 87 | 87 | none open (P2-138 done) |
| ArrayCreation | 75 | 75 | none |
| CaughtException | 61 | 61 | none open (P2-180 done) |
| CompoundAssignment | 55 | 55 | none open (P2-141 done) |
| Tuple | 43 | 43 | none |
| ref-argument | 38 | 38 | none |
| None | 24 | 24 | none |
| IsType | 21 | 21 | none |

## Changed code
- Changed pairs 225 of 10120; without opaque 129; whole-body opaque 14
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 57.3%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 129 | n/a |
| "CaughtException" | 14 | none open (P2-180 done) |
| "ArrayElementReference" | 10 | P2-183 (OpenRA forms) |
| "DelegateCreation" | 10 | none open (P2-182 done) |
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
- By rule: EQ001 9929, EQ002 0, EQ003 38, EQ004 31, EQ005 62, EQ006 1, EQ007 152 (witnessed 33, not 119)
- Runtime exposures (EQ007, ADR 0060): an unedited body a runtime change reaches. Witnessed ones carry `properties.model` and count as Divergent for the exit code; the others carry `unknownReason` and count as Unknown. Divergent is EQ002 + EQ006 alone, and Unknown EQ003 alone, so the three never overlap.
- By proofMethod, Equivalent results: congruence 9895, bounded 18, lockstep-induction 16
- Changed pairs by outcome: proved Equivalent by the solver 34 (15.1%), Unknown 38 (16.9%), Divergent 1 (0.4%; EQ002 0, EQ006 1), runtime exposure 152 (67.6%; witnessed 33, not 119). `unmatched-overload` Unknowns, not matched pairs: 0
- Unknown by scope (EQ003): line 9, method 29. Line-scoped Unknown share: 23.7%
- Top Unknown reasons (EQ003): opaque 16, timeout 10, abstraction 9, recursion 2, unaligned-loop 1
- Runtime exposures without a witness, by `unknownReason`: timeout 53, abstraction 41, opaque 20, unaligned-loop 5; by first rule (up to 8): `System.IO.Path::Combine(string,string)` 16, `System.String::StartsWith(` 13, `System.Math::Min(float,float)` 13, `System.Text.Encoding::GetString(` 8, `System.IO.Path::GetFileNameWithoutExtension(string)` 6, `System.Math::Max(float,float)` 6, `System.IO.File::OpenRead(string)` 6, `extern` 6
- Runtime exposures with a witness, by first rule (up to 8): `System.IO.File::OpenRead(string)` 5, `System.IO.Path::Combine(string,string)` 4, `System.String::StartsWith(` 4, `System.Math::Min(float,float)` 3, `System.Text.Encoding::GetString(` 2, `System.Math::Max(float,float)` 2, `System.IO.Path::Combine(string,string,string,string)` 2, `System.Net.WebClient::CancelAsync(` 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 149, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 152, callee 63, operation 5, type-layout 4, symbol 1. Of the 152 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 0 EQ002 and 152 EQ007; none is EQ006 or EQ003, as ADR 0060 decision 2 has it.
- `unbound` Unknowns: 0
- Top abstractions, over every `abstraction` result (EQ003 and unwitnessed EQ007): delegate 141, `System.String::Concat(string,string)` 28, `op:System.Reflection.FieldInfo::op_Equality(System.Reflection.FieldInfo,System.Reflection.FieldInfo)` 6, `op:System.String::op_Equality(string,string)` 5, `System.String::IsNullOrEmpty(string)` 4, `System.String::Split(char,System.StringSplitOptions)` 4, opaque DefaultValue 4, `System.String::Substring(int,int)` 4, `System.String::Substring(int)` 4, `System.String::StartsWith(string,System.StringComparison)` 4, `f32.mul` 4, `conv.i32.f64` 4, `f64.div` 4, opaque Conversion 4, `get:System.String::get_Length()` 3
- Review list: 51 groups for 191 flagged results (EQ002 + EQ003 + EQ006 + EQ007), from `run.properties.reviewList`; flagged results as a share of matched pairs: 1.9%. Top five: `EQ007 runtime-change:System.IO.Path::Combine(string,string)`: 20, `EQ007 runtime-change:System.String::StartsWith(`: 17, `EQ007 runtime-change:System.Math::Min(float,float)`: 14, `EQ007 runtime-change:System.IO.File::OpenRead(string)`: 10, `EQ007 runtime-change:System.Math::Max(float,float)`: 10
- Divergent adjudication: `docs/runs/2026-10-17-runtime-exposure-verdict.md` (P2-212's sample, with P2-237's execution, split by these runs' rule ids).

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-237 (`de93b9c7`): its 33 EQ006 and 119 Unknown on `runtime-rule` pairs are the 33 witnessed and 119 unwitnessed EQ007, exactly, as ADR 0060 projected (152). EQ006 34 to 1, the one whose text differs; EQ002 0; no `runtime-rule` pair is EQ002. Off the exposures, Unknown 43 to 38 and the solver proves 34, from 29: P2-182 lowered the largest DelegateCreation form.
