# full run: openra-17989

- Pair: human, OpenRA/OpenRA PR #17989 (Mono/net472 plus .NET 5), legacy fef7a018f2b2, modern 5e74e58b2204
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: bceeea62, mode full with `--execute`, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, committed config `tools/corpus/configs/openra-17989.json` (P2-197), wall-clock 251s (4m11s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-245: the three migration pairs one after the other from one Release build, on the checkouts and restores P2-237 made, nothing else started from this session while they ran. The last summary of this pair is `docs/runs/2026-10-17-full-openra-17989/SUMMARY.md`; `docs/runs/2026-10-19-execution-reach.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 9 | 0.001 | +0.000 |
| load-modern | 8 | 0.000 | +0.000 |
| enumerate | 2 | 0.526 | n/a |
| match | 1 | 0.016 | n/a |
| lower | 10120 | 74.050 | -3.537 |
| verify | 10120 | 157.380 | -74.044 |
| execute | 10120 | 5.931 | +5.298 |
| write | 1 | 0.208 | +0.000 |

## Load
- Projects: legacy 9 with a detected runtime, modern 8; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy OpenRA.WindowsLauncher; modern OpenRA.Test, OpenRA.WindowsLauncher
- Runtimes detected (`run.properties.runtimes`): legacy net472 (9) [source: attribute 9]; modern net5.0 (8) [source: attribute 3, host 1, config 4]
- Execution: legacy on the .NET Framework 4.8 CLR (an `app.config` naming each project's net472 sku), modern on `Microsoft.NETCore.App` 5.0.17 (`rollForward: Disable`). Replay and `probe` drivers run on `Microsoft.NETCore.App` only, and this host has no `Microsoft.WindowsDesktop.App` 5.0, so a modern member that loads a Windows Forms type is not constructible there (P2-253).

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
| DelegateCreation | 101 | 97 | P2-182 (OpenRA forms; P2-136 done) |
| DefaultValue | 87 | 87 | none open (P2-138 done) |
| ArrayCreation | 75 | 75 | none |
| CaughtException | 61 | 61 | P2-180 |
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
| "CaughtException" | 14 | P2-180 |
| "ArrayElementReference" | 10 | P2-183 (OpenRA forms) |
| "DelegateCreation" | 10 | P2-182 (OpenRA forms; P2-136 done) |
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
- By rule: EQ001 9929, EQ002 0, EQ003 38, EQ004 31, EQ005 62, EQ006 2, EQ007 151 (witnessed 32, not 119)
- Runtime exposures (EQ007, ADR 0060): an unedited body a runtime change reaches. Witnessed ones carry `properties.model` and count as Divergent for the exit code; the others carry `unknownReason` and count as Unknown. Divergent is EQ002 + EQ006 alone, and Unknown EQ003 alone, so the three never overlap.
- By proofMethod, Equivalent results: congruence 9895, bounded 18, lockstep-induction 16
- Changed pairs by outcome: proved Equivalent by the solver 34 (15.1%), Unknown 38 (16.9%), Divergent 2 (0.9%; EQ002 0, EQ006 2), runtime exposure 151 (67.1%; witnessed 32, not 119). An exposure whose replay is `reproduced` is EQ006 (ADR 0060 decision 4): 1 of the EQ006 here. `unmatched-overload` Unknowns, not matched pairs: 0
- Unknown by scope: line 9, method 29. Line-scoped Unknown share: 23.7%
- Top Unknown reasons: opaque 16, timeout 10, abstraction 9, recursion 2, unaligned-loop 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 149, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 152, callee 63, operation 5, type-layout 4, symbol 1. Of the 152 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 151 EQ007, 1 EQ006, 0 EQ002 and 0 Unknown ().
- `unbound` Unknowns: 0
- Top abstractions: delegate 20, `System.String::Concat(string,string)` 8, `System.String::Split(char,System.StringSplitOptions)` 4, `System.String::IsNullOrEmpty(string)` 2, `op:System.Reflection.MethodInfo::op_Equality(System.Reflection.MethodInfo,System.Reflection.MethodInfo)` 2, `System.String::IsNullOrWhiteSpace(string)` 2, `op:System.String::op_Equality(string,string)` 2, `System.String::Substring(int)` 1, `System.String::Contains(char)` 1
- Review list: 51 groups for 191 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 1.9%. Top five: `EQ007 runtime-change:System.IO.Path::Combine(string,string)`: 20, `EQ007 runtime-change:System.String::StartsWith(`: 17, `EQ007 runtime-change:System.Math::Min(float,float)`: 14, `EQ007 runtime-change:System.IO.File::OpenRead(string)`: 10, `EQ007 runtime-change:System.Math::Max(float,float)`: 10
- Replay (`properties.replay`, ADR 0035 decision 2), EQ002 0: none
- Replay (`properties.replay`, ADR 0035 decision 2), EQ006 2: not-applicable 1, reproduced 1
- Replay (`properties.replay`, ADR 0035 decision 2), EQ007 151: none 119, not-applicable 1, not-constructible 31
- Why a replay is not constructible (EQ002, EQ006 and witnessed EQ007): the divergence is in the call trace 19, the call traces differ before the outcome 7, the model constrains field (a static or undeclared field, typeof, caller or thrown) 3, a framework or third-party object with no public parameterless constructor, or with model fields 1, a side gave no comparable outcome 1
- Divergent adjudication: `docs/runs/2026-10-19-execution-reach.md` (P2-245), over `docs/runs/2026-10-17-precision-execute-verdict.md` (P2-237).

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-238 (`b0f56ff8`, no `--execute`): every count is the same but EQ006 1 to 2 and witnessed EQ007 33 to 32: `Platform::get_RuntimeVersion()`, whose replay is `reproduced`, is EQ006 (ADR 0060 decision 4). The solver proves 34, as before.
- Replay: EQ006 2, 1 `reproduced` and 1 `not-applicable`; witnessed EQ007 32, 1 `not-applicable` and 31 `not-constructible` (19 only in the call trace, 7 where the call traces split). No `not-reproduced`.
- P2-237's 8 uncallable or unbuilt-argument sample rows re-adjudicated: all 8 callable (1 not run: a network download), 6 confirmed, 2 still undetermined; precision over the sampled results still Divergent 53.3% (8 of 15), from 22.2% (2 of 9).
