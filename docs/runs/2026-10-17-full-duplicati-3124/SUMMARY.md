# full run: duplicati-3124

- Pair: human, duplicati/duplicati PR #3124 (.NET Framework 4.7.1 to .NET 5, first step), legacy beaf03562fdc, modern 4f565a004f52
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: b0f56ff8, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, committed config `tools/corpus/configs/duplicati-3124.json`, wall-clock 613s (10m13s), exit 1. Exit 1 means Divergent results exist (here a witnessed runtime exposure counts as one, ADR 0060 decision 5). No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-238: the five large pairs one after the other from one Release build, on the checkouts P2-212 fetched and restored, nothing else started from this session while they ran. The last summary of this pair is `docs/runs/2026-10-17-full-execute-duplicati-3124/SUMMARY.md`; `docs/runs/2026-10-17-runtime-exposure-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 51 | 0.001 | +0.001 |
| load-modern | 52 | 0.000 | +0.000 |
| enumerate | 2 | 0.253 | n/a |
| match | 1 | 0.010 | n/a |
| lower | 6294 | 47.500 | +5.857 |
| verify | 6294 | 536.302 | -278.751 |
| write | 1 | 0.163 | +0.000 |

## Load
- Projects: legacy 51 with a detected runtime, modern 52; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Runtimes detected (`run.properties.runtimes`): legacy net471 (51) [source: attribute 51]; modern net5.0 (52) [source: attribute 52]

## Census
| | legacy | modern |
|---|---|---|
| procedures | 6359 | 6415 |
| analysed lines | 68706 | 69205 |

- Matched pairs 6294; without opaque 5534 (87.9%); whole-body opaque 119 (1.9%); congruent 5654 (89.8%)
- Unchanged share: 89.8% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 421 of 739 legacy `.cs` files (77615 of 125501 lines, 61.8%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 89.8%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| CaughtException | 182 | 182 | none open (P2-180 done) |
| iterator | 99 | 99 | none |
| rethrow | 94 | 95 | none |
| Binary | 91 | 91 | none open (P2-087 done) |
| TranslatedQuery | 69 | 69 | none |
| Conversion | 67 | 67 | none open (P2-123 done) |
| ArrayCreation | 54 | 54 | none |
| InstanceReference | 51 | 51 | none |
| call-throw-in-try | 40 | 40 | none |
| DelegateCreation | 42 | 37 | none open (P2-182 done) |
| ref-argument | 39 | 39 | none |
| Layout | 29 | 37 | none |
| rebound-call | 28 | 28 | none open (P2-179 done) |
| Throw | 24 | 24 | none |
| ArrayElementReference | 22 | 22 | P2-183 (OpenRA forms) |

## Changed code
- Changed pairs 640 of 6294; without opaque 384; whole-body opaque 41
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 60.0%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 384 | n/a |
| "CaughtException" | 25 | none open (P2-180 done) |
| "iterator" | 21 | none |
| "CaughtException+rethrow" | 19 | none open (P2-180 done); none |
| "no-body" | 19 | P2-214 |
| "rebound-call" | 14 | none open (P2-179 done) |
| "Binary" | 10 | none open (P2-087 done) |
| "TranslatedQuery" | 9 | none |
| "Conversion" | 8 | none open (P2-123 done) |
| "Layout" | 8 | none |
| "ArrayCreation" | 7 | none |
| "CaughtException+Conversion" | 7 | none open (P2-180 done); none open (P2-123 done) |
| "ref-argument" | 7 | none |
| "CaughtException+Conversion+rethrow" | 6 | none open (P2-180 done); none open (P2-123 done); none |
| "DelegateCreation" | 6 | none open (P2-182 done) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 1032 | 1029 |
| distinct members | 70 | 71 |
| pairs with any | 431 | 430 |

- Package changes: 16 version changed, 9 legacy only, 145 modern only; `alphavss 1.4.0 -> 2.0.0`, `fluentftp 27.1.1 -> 28.0.5`, `mailkit 2.3.1.6 -> 2.4.1`, `microsoft.rest.clientruntime.azure 3.3.19 -> 3.3.18`, `mimekit 2.3.1 -> 2.4.1`, `minio 3.1.7 -> 3.1.8`, `newtonsoft.json 12.0.2 -> 12.0.3`, `ngettext 0.6.4 -> 0.6.5`, `sharpaescrypt.dll 1.3.3 -> 1.3.4`, `ssh.net 2020.0.1 -> 2016.1.0, 2020.0.1`, `system.reactive 4.0.0 -> 4.1.2, 4.3.2`, `system.reactive.linq 4.0.0 -> 4.3.2` (first 15 of 16)

## Verdicts (full and seeded only)
- By rule: EQ001 5878, EQ002 13, EQ003 174, EQ004 121, EQ005 65, EQ006 14, EQ007 221 (witnessed 91, not 130)
- Runtime exposures (EQ007, ADR 0060): an unedited body a runtime change reaches. Witnessed ones carry `properties.model` and count as Divergent for the exit code; the others carry `unknownReason` and count as Unknown. Divergent is EQ002 + EQ006 alone, and Unknown EQ003 alone, so the three never overlap.
- By proofMethod, Equivalent results: congruence 5654, bounded 186, lockstep-induction 38
- Changed pairs by outcome: proved Equivalent by the solver 224 (35.0%), Unknown 168 (26.2%), Divergent 27 (4.2%; EQ002 13, EQ006 14), runtime exposure 221 (34.5%; witnessed 91, not 130). `unmatched-overload` Unknowns, not matched pairs: 6
- Unknown by scope (EQ003): line 60, method 108. Line-scoped Unknown share: 35.7%
- Top Unknown reasons (EQ003): opaque 92, timeout 57, abstraction 14, unaligned-loop 4, recursion 1
- Runtime exposures without a witness, by `unknownReason`: timeout 50, abstraction 47, opaque 30, unaligned-loop 3; by first rule (up to 8): `System.IO.Path::Combine(string,string)` 22, `extern` 19, `System.IO.File::Delete(string)` 10, `System.IO.File::OpenRead(string)` 6, `System.IO.Path::GetFileName(string)` 6, `System.IO.Path::GetFullPath(string)` 5, `System.IO.Directory::CreateDirectory(string)` 5, `System.String::Equals(string,string,System.StringComparison)` 4
- Runtime exposures with a witness, by first rule (up to 8): `System.IO.File::OpenRead(string)` 17, `System.IO.File::Create(string)` 15, `System.IO.Path::Combine(string,string)` 11, `System.Threading.Thread::Abort(` 5, `System.IO.File::GetLastWriteTimeUtc(string)` 4, `System.IO.File::Delete(string)` 4, `System.IO.Path::GetFullPath(string)` 4, `System.IO.Directory::CreateDirectory(string)` 3
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 228, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): operation 332, runtime-rule 221, callee 78, symbol 5, type 2, signature 1, constant 1. Of the 221 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 0 EQ002 and 221 EQ007; none is EQ006 or EQ003, as ADR 0060 decision 2 has it.
- `unbound` Unknowns: 0
- Top abstractions, over every `abstraction` result (EQ003 and unwitnessed EQ007): delegate 51, `System.String::Concat(string,string)` 30, `System.String::IsNullOrEmpty(string)` 20, `System.String::IsNullOrWhiteSpace(string)` 20, `get:System.String::get_Length()` 13, `System.String::StartsWith(string,System.StringComparison)` 11, `op:Duplicati.Library.Utility.TempFile::op_Implicit(Duplicati.Library.Utility.TempFile)` 10, `System.String::Substring(int,int)` 9, `System.String::get_Chars(int)` 6, `System.String::Equals(string,string,System.StringComparison)` 6, `op:System.String::op_Equality(string,string)` 5, `f64.div` 4, `System.String::Trim()` 4, `op:Duplicati.Library.Utility.TempFolder::op_Implicit(Duplicati.Library.Utility.TempFolder)` 4, `System.Int32::ToString()` 4
- Review list: 94 groups for 422 flagged results (EQ002 + EQ003 + EQ006 + EQ007), from `run.properties.reviewList`; flagged results as a share of matched pairs: 6.7%. Top five: `EQ003 timeout`: 57, `EQ007 runtime-change:System.IO.Path::Combine(string,string)`: 36, `EQ007 runtime-change:System.IO.File::OpenRead(string)`: 22, `EQ003 opaque:CaughtException+rethrow`: 20, `EQ007 runtime-change:extern`: 19
- Divergent adjudication: `docs/runs/2026-10-17-runtime-exposure-verdict.md` (P2-212's sample, with P2-237's execution, split by these runs' rule ids).

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-237 (`de93b9c7`): its 91 EQ006 and 130 Unknown on `runtime-rule` pairs are the 91 witnessed and 130 unwitnessed EQ007, exactly; EQ006 105 to 14. EQ002 13 as before; no `runtime-rule` pair is EQ002. Off the exposures, Unknown 179 to 168 and the solver proves 11 more (224): P2-180 lowered the largest caught-exception use. ADR 0060 projected 236 exposures from P2-212's run; P2-212 to P2-237 cleared 15 (the same path and row tickets), so 221.
- Wall-clock 613 s, from 447 s at P2-212 and 563 s at P2-237 with `--execute`: `verify` took 536 s (469 s, 380 s), the pairs P2-180 lowered now reaching the solver, on top of the slowdown P2-244 owns. No new ticket: P2-244 measures the two commits back to back.
