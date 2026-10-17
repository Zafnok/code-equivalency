# full run: duplicati-3124

- Pair: human, duplicati/duplicati PR #3124 (.NET Framework 4.7.1 to .NET 5, first step), legacy beaf03562fdc, modern 4f565a004f52
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: de93b9c7, mode full with `--execute`, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, committed config `tools/corpus/configs/duplicati-3124.json` (P2-213: three `callIdentityRenames`), wall-clock 563s (9m23s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-237: the three migration pairs one after the other from one build, nothing else started from this session while they ran, with the .NET 5.0.17 runtime installed for these runs. The last summary of this pair is `docs/runs/2026-10-15-full-duplicati-3124/SUMMARY.md`; `docs/runs/2026-10-17-precision-execute-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 51 | 0.001 | +0.001 |
| load-modern | 52 | 0.000 | +0.000 |
| enumerate | 2 | 0.286 | n/a |
| match | 1 | 0.010 | n/a |
| lower | 6294 | 48.414 | +10.420 |
| verify | 6294 | 469.433 | -132.448 |
| execute | 6294 | 9.061 | -2.745 |
| write | 1 | 0.236 | +0.000 |

## Load
- Projects: legacy 51 with a detected runtime, modern 52; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Runtimes detected (`run.properties.runtimes`): legacy net471 (51) [source: attribute 51]; modern net5.0 (52) [source: attribute 52]
- Execution: legacy on the .NET Framework 4.8 CLR (an `app.config` naming each project's net471 sku), modern on `Microsoft.NETCore.App` 5.0.17 (`rollForward: Disable`). Replay drivers run on `Microsoft.NETCore.App` only, so no `Microsoft.WindowsDesktop.App` 5.0 was needed or installed.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 6359 | 6415 |
| analysed lines | 68706 | 69205 |

- Matched pairs 6294; without opaque 5514 (87.6%); whole-body opaque 119 (1.9%); congruent 5654 (89.8%)
- Unchanged share: 89.8% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 421 of 739 legacy `.cs` files (77615 of 125501 lines, 61.8%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 89.8%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| CaughtException | 193 | 193 | P2-180 |
| iterator | 99 | 99 | none |
| rethrow | 94 | 95 | none |
| Binary | 91 | 91 | none open (P2-087 done) |
| TranslatedQuery | 69 | 69 | none |
| Conversion | 67 | 67 | none open (P2-123 done) |
| ArrayCreation | 54 | 54 | none |
| DelegateCreation | 56 | 52 | P2-182 (OpenRA forms; P2-136 done) |
| InstanceReference | 51 | 51 | none |
| call-throw-in-try | 40 | 40 | none |
| ref-argument | 39 | 39 | none |
| Layout | 29 | 37 | none |
| rebound-call | 28 | 28 | P2-179 |
| Throw | 24 | 24 | none |
| ArrayElementReference | 22 | 22 | P2-183 (OpenRA forms) |

## Changed code
- Changed pairs 640 of 6294; without opaque 370; whole-body opaque 41
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 57.8%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 370 | n/a |
| "CaughtException" | 35 | P2-180 |
| "iterator" | 21 | none |
| "CaughtException+rethrow" | 19 | P2-180; none |
| "no-body" | 19 | P2-214 |
| "rebound-call" | 14 | P2-179 |
| "Binary" | 10 | none open (P2-087 done) |
| "DelegateCreation" | 10 | P2-182 (OpenRA forms; P2-136 done) |
| "TranslatedQuery" | 9 | none |
| "Conversion" | 8 | none open (P2-123 done) |
| "Layout" | 8 | none |
| "ArrayCreation" | 7 | none |
| "CaughtException+Conversion" | 7 | P2-180; none open (P2-123 done) |
| "ref-argument" | 7 | none |
| "CaughtException+Conversion+rethrow" | 6 | P2-180; none open (P2-123 done); none |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 1032 | 1029 |
| distinct members | 70 | 71 |
| pairs with any | 431 | 430 |

- Package changes: 16 version changed, 9 legacy only, 145 modern only; `alphavss 1.4.0 -> 2.0.0`, `fluentftp 27.1.1 -> 28.0.5`, `mailkit 2.3.1.6 -> 2.4.1`, `microsoft.rest.clientruntime.azure 3.3.19 -> 3.3.18`, `mimekit 2.3.1 -> 2.4.1`, `minio 3.1.7 -> 3.1.8`, `newtonsoft.json 12.0.2 -> 12.0.3`, `ngettext 0.6.4 -> 0.6.5`, `sharpaescrypt.dll 1.3.3 -> 1.3.4`, `ssh.net 2020.0.1 -> 2016.1.0, 2020.0.1`, `system.reactive 4.0.0 -> 4.1.2, 4.3.2`, `system.reactive.linq 4.0.0 -> 4.3.2` (first 15 of 16; unchanged since P2-212)

## Verdicts (full and seeded only)
- By rule: EQ001 5867, EQ002 13, EQ003 315, EQ004 121, EQ005 65, EQ006 105
- By proofMethod, Equivalent results: congruence 5654, bounded 179, lockstep-induction 34
- Changed pairs by outcome: proved Equivalent by the solver 213 (33.3%), Unknown 309 (48.3%), Divergent 118 (18.4%; EQ002 13, EQ006 105). `unmatched-overload` Unknowns, not matched pairs: 6
- Unknown by scope: line 71, method 238. Line-scoped Unknown share: 23.0%
- Top Unknown reasons: opaque 129, timeout 111, abstraction 61, unaligned-loop 7, recursion 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 233, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): operation 332, runtime-rule 221, callee 78, symbol 5, type 2, signature 1, constant 1. Of the 221 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 91 are EQ006, 0 EQ002 and 130 Unknown (timeout 50, abstraction 47, opaque 30, unaligned-loop 3).
- `unbound` Unknowns: 0
- Top abstractions: delegate 51, `System.String::Concat(string,string)` 30, `System.String::IsNullOrEmpty(string)` 20, `System.String::IsNullOrWhiteSpace(string)` 20, `get:System.String::get_Length()` 13, `System.String::StartsWith(string,System.StringComparison)` 11, `op:Duplicati.Library.Utility.TempFile::op_Implicit(Duplicati.Library.Utility.TempFile)` 10, `System.String::Substring(int,int)` 9, `System.String::get_Chars(int)` 6, `System.String::Equals(string,string,System.StringComparison)` 6, `op:System.String::op_Equality(string,string)` 5, `f64.div` 4, `System.String::Trim()` 4, `op:Duplicati.Library.Utility.TempFolder::op_Implicit(Duplicati.Library.Utility.TempFolder)` 4, `System.Int32::ToString()` 4
- Review list: 77 groups for 433 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 6.9%. Top five: `EQ003 timeout`: 111, `EQ003 abstraction`: 56, `EQ003 opaque:CaughtException`: 21, `EQ003 opaque:CaughtException+rethrow`: 21, `EQ003 opaque:iterator`: 21
- Replay (`properties.replay`, ADR 0035 decision 2), EQ002 13: not-constructible 13
- Replay (`properties.replay`, ADR 0035 decision 2), EQ006 105: not-applicable 6, not-constructible 91, reproduced 8
- Why a replay is not constructible: the divergence is in the call trace 48, the model constrains a field, cast or array 17, not public 17, no public parameterless constructor on the receiver type 9, a side gave no comparable outcome 5, the call traces differ before the outcome 5, not a method or a property getter 2, both sides threw where the model does not 1
- Divergent adjudication: `docs/runs/2026-10-17-precision-execute-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-212 (`bfbf413e`, no config): EQ002 23 to 13 and EQ006 117 to 105; the solver proves 213 changed pairs, from 199. P2-213's committed config makes 8 of the 9 P2-192 results in P2-212's sample Equivalent (bounded); `LocalizationContext::.ctor(CultureInfo)` is still EQ002, as P2-213's Notes record.
- Replay: every EQ002 (13) is `not-constructible`; of 105 EQ006, 8 `reproduced`, 6 `not-applicable`, 91 `not-constructible`. No `not-reproduced`.
- P2-212's sample executed (53 rows, 32 undetermined): 10 confirmed (9 by `probe`, `SystemIOWindows::GetFileAttributes(string)` by its replay), 2 no longer Divergent (`Metahash::.ctor`, `HashCalculatingStream::.ctor`, now bounded), 20 still undetermined; precision over the results still Divergent here 50.0% (10 of 20).
- `WindowsPowerSupplyState::GetSource()` threw `FileNotFoundException` on the modern side under `probe`: a driver copies no implementation of a package it compiled against a reference assembly (`System.Management`), so a replay can report a difference the solution would not show: P2-240 (new).
- `CaughtException` alone in 35 of 640 changed pairs (5.5%): P2-180.
