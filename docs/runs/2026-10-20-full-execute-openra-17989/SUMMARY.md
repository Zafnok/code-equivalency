# full run: openra-17989

- Pair: human, OpenRA/OpenRA PR #17989 (Mono/net472 plus .NET 5), legacy fef7a018f2b2, modern 5e74e58b2204
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: 4b2e0df6, mode full with `--execute`, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, committed config `tools/corpus/configs/openra-17989.json` (P2-197), wall-clock 263s (4m23s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-247: the five large pairs one after the other from one Release build of `main`, on the checkouts and restores P2-237 made, nothing else started on this machine while they ran, with a private `dotnet` root holding the runtimes the pairs need (`docs/runs/2026-10-20-precision-execute-verdict-2.md`). The last summary of this pair is `docs/runs/2026-10-19-full-execute-openra-17989/SUMMARY.md`; the verdict sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 9 | 0.001 | +0.000 |
| load-modern | 8 | 0.000 | +0.000 |
| enumerate | 2 | 0.417 | n/a |
| match | 1 | 0.017 | n/a |
| lower | 10120 | 76.800 | -7.238 |
| verify | 10120 | 166.479 | -79.121 |
| execute | 10120 | 6.226 | +4.522 |
| write | 1 | 0.194 | +0.000 |

## Load
- Projects: legacy 9 with a detected runtime, modern 8; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy OpenRA.WindowsLauncher; modern OpenRA.Test, OpenRA.WindowsLauncher
- Runtimes detected (`run.properties.runtimes`): legacy net472 (9) [source: attribute 9]; modern net5.0 (8) [source: attribute 3, host 1, config 4]
- Execution: legacy on the .NET Framework 4.8 CLR (an `app.config` naming each project's net472 sku), modern on `Microsoft.NETCore.App` 5.0.17 (`rollForward: Disable`); a project that uses the Windows Desktop framework runs on `Microsoft.WindowsDesktop.App` 5.0.17 (P2-253), from the private root.

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
- By rule: EQ001 9929, EQ002 0, EQ003 38, EQ004 31, EQ005 62, EQ006 2, EQ007 151 (witnessed 33, not 118)
- Runtime exposures (EQ007, ADR 0060): an unedited body a runtime change reaches. Witnessed ones carry `properties.model` and count as Divergent for the exit code; the others carry `unknownReason` and count as Unknown. Divergent is EQ002 + EQ006 alone, and Unknown EQ003 alone, so the three never overlap.
- By proofMethod, Equivalent results: congruence 9895, bounded 18, lockstep-induction 16
- Changed pairs by outcome: proved Equivalent by the solver 34 (15.1%), Unknown 38 (16.9%), Divergent 2 (0.9%; EQ002 0, EQ006 2), runtime exposure 151 (67.1%; witnessed 33, not 118). An exposure whose replay is `reproduced` is EQ006 (ADR 0060 decision 4): 1 of the EQ006 here. `unmatched-overload` Unknowns, not matched pairs: 0
- Unknown by scope: line 9, method 29. Line-scoped Unknown share: 23.7%
- Top Unknown reasons: opaque 16, timeout 10, abstraction 9, recursion 2, unaligned-loop 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 147, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 152, callee 63, operation 5, type-layout 4, symbol 1. Of the 152 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 151 EQ007, 1 EQ006, 0 EQ002 and 0 Unknown ().
- `unbound` Unknowns: 0
- Top abstractions: delegate 20, `System.String::Concat(string,string)` 8, `System.String::Split(char,System.StringSplitOptions)` 4, `System.String::IsNullOrEmpty(string)` 2, `op:System.Reflection.MethodInfo::op_Equality(System.Reflection.MethodInfo,System.Reflection.MethodInfo)` 2, `System.String::IsNullOrWhiteSpace(string)` 2, `op:System.String::op_Equality(string,string)` 2, `System.String::Substring(int)` 1, `System.String::Contains(char)` 1
- Review list: 51 groups for 191 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 1.9%. Top five: `EQ007 runtime-change:System.IO.Path::Combine(string,string)`: 20, `EQ007 runtime-change:System.String::StartsWith(`: 17, `EQ007 runtime-change:System.Math::Min(float,float)`: 14, `EQ007 runtime-change:System.IO.File::OpenRead(string)`: 10, `EQ007 runtime-change:System.Math::Max(float,float)`: 10
- Replay (`properties.replay`, ADR 0035 decision 2), EQ002 0: none
- Replay (`properties.replay`, ADR 0035 decision 2), EQ006 2: not-applicable 1, reproduced 1
- Replay (`properties.replay`, ADR 0035 decision 2), EQ007 151: none 118, not-applicable 1, not-constructible 32
- Why a replay is not constructible (EQ002, EQ006 and witnessed EQ007): the divergence is in the call trace 20, the call traces differ before the outcome 7, the model constrains field (a static or undeclared field, typeof, caller or thrown) 3, a framework or third-party object with no public parameterless constructor, or with model fields 1, a side gave no comparable outcome 1
- Divergent and exposure adjudication: `docs/runs/2026-10-20-precision-execute-verdict-2.md` (P2-247).

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry. Every count is P2-238's but one witnessed exposure, `Platform::get_RuntimeVersion()`, which is EQ006 here because its replay is `reproduced` (ADR 0060 decision 4).
- Replay: no `not-reproduced`. The default EQ006 (`ChatCommands::OnChat(string,string)`) is `not-applicable`; the observed one `reproduced`.
- Adjudicated: both EQ006 confirmed. Of P2-237's 23 rows here, 16 confirmed, 1 false positive, 4 undetermined after this audit (10 confirmed and 1 refuted by it, with the pair config passed to `probe`); of 20 sampled exposures (every one of the 5 witnessed outside P2-212's sample, and 15 unwitnessed) 3 confirmed, 5 false positives, 12 undetermined.
- False-positive causes filed by P2-247: P2-262, P2-266.
