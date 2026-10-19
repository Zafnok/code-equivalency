# full run: duplicati-3124

- Pair: human, duplicati/duplicati PR #3124 (.NET Framework 4.7.1 to .NET 5, first step), legacy beaf03562fdc, modern 4f565a004f52
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: bceeea62, mode full with `--execute`, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, committed config `tools/corpus/configs/duplicati-3124.json` (P2-213: three `callIdentityRenames`), wall-clock 584s (9m44s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-245: the three migration pairs one after the other from one Release build, on the checkouts and restores P2-237 made, nothing else started from this session while they ran. The last summary of this pair is `docs/runs/2026-10-17-full-duplicati-3124/SUMMARY.md`; `docs/runs/2026-10-19-execution-reach.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 51 | 0.001 | +0.001 |
| load-modern | 52 | 0.000 | +0.000 |
| enumerate | 2 | 0.253 | n/a |
| match | 1 | 0.010 | n/a |
| lower | 6294 | 41.352 | +9.266 |
| verify | 6294 | 493.007 | -248.073 |
| execute | 6294 | 22.104 | -7.475 |
| write | 1 | 0.148 | +0.000 |

## Load
- Projects: legacy 51 with a detected runtime, modern 52; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Runtimes detected (`run.properties.runtimes`): legacy net471 (51) [source: attribute 51]; modern net5.0 (52) [source: attribute 52]
- Execution: legacy on the .NET Framework 4.8 CLR (an `app.config` naming each project's net471 sku), modern on `Microsoft.NETCore.App` 5.0.17 (`rollForward: Disable`). Replay and `probe` drivers run on `Microsoft.NETCore.App` only, and this host has no `Microsoft.WindowsDesktop.App` 5.0, so a modern member that loads a Windows Forms type is not constructible there (P2-253).

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
| CaughtException | 182 | 182 | P2-180 |
| iterator | 99 | 99 | none |
| rethrow | 94 | 95 | none |
| Binary | 91 | 91 | none open (P2-087 done) |
| TranslatedQuery | 69 | 69 | none |
| Conversion | 67 | 67 | none open (P2-123 done) |
| ArrayCreation | 54 | 54 | none |
| InstanceReference | 51 | 51 | none |
| call-throw-in-try | 40 | 40 | none |
| DelegateCreation | 42 | 37 | P2-182 (OpenRA forms; P2-136 done) |
| ref-argument | 39 | 39 | none |
| Layout | 29 | 37 | none |
| rebound-call | 28 | 28 | P2-179 |
| Throw | 24 | 24 | none |
| ArrayElementReference | 22 | 22 | P2-183 (OpenRA forms) |

## Changed code
- Changed pairs 640 of 6294; without opaque 384; whole-body opaque 41
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 60.0%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 384 | n/a |
| "CaughtException" | 25 | P2-180 |
| "iterator" | 21 | none |
| "CaughtException+rethrow" | 19 | P2-180; none |
| "no-body" | 19 | P2-214 |
| "rebound-call" | 14 | P2-179 |
| "Binary" | 10 | none open (P2-087 done) |
| "TranslatedQuery" | 9 | none |
| "Conversion" | 8 | none open (P2-123 done) |
| "Layout" | 8 | none |
| "ArrayCreation" | 7 | none |
| "CaughtException+Conversion" | 7 | P2-180; none open (P2-123 done) |
| "ref-argument" | 7 | none |
| "CaughtException+Conversion+rethrow" | 6 | P2-180; none open (P2-123 done); none |
| "DelegateCreation" | 6 | P2-182 (OpenRA forms; P2-136 done) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 1032 | 1029 |
| distinct members | 70 | 71 |
| pairs with any | 431 | 430 |

- Package changes: 16 version changed, 9 legacy only, 145 modern only; `alphavss 1.4.0 -> 2.0.0`, `fluentftp 27.1.1 -> 28.0.5`, `mailkit 2.3.1.6 -> 2.4.1`, `microsoft.rest.clientruntime.azure 3.3.19 -> 3.3.18`, `mimekit 2.3.1 -> 2.4.1`, `minio 3.1.7 -> 3.1.8`, `newtonsoft.json 12.0.2 -> 12.0.3`, `ngettext 0.6.4 -> 0.6.5`, `sharpaescrypt.dll 1.3.3 -> 1.3.4`, `ssh.net 2020.0.1 -> 2016.1.0, 2020.0.1`, `system.reactive 4.0.0 -> 4.1.2, 4.3.2`, `system.reactive.linq 4.0.0 -> 4.3.2` (first 15 of 16; unchanged since P2-212)

## Verdicts (full and seeded only)
- By rule: EQ001 5878, EQ002 13, EQ003 174, EQ004 121, EQ005 65, EQ006 24, EQ007 211 (witnessed 81, not 130)
- Runtime exposures (EQ007, ADR 0060): an unedited body a runtime change reaches. Witnessed ones carry `properties.model` and count as Divergent for the exit code; the others carry `unknownReason` and count as Unknown. Divergent is EQ002 + EQ006 alone, and Unknown EQ003 alone, so the three never overlap.
- By proofMethod, Equivalent results: congruence 5654, bounded 186, lockstep-induction 38
- Changed pairs by outcome: proved Equivalent by the solver 224 (35.0%), Unknown 168 (26.2%), Divergent 37 (5.8%; EQ002 13, EQ006 24), runtime exposure 211 (33.0%; witnessed 81, not 130). An exposure whose replay is `reproduced` is EQ006 (ADR 0060 decision 4): 10 of the EQ006 here. `unmatched-overload` Unknowns, not matched pairs: 6
- Unknown by scope: line 60, method 108. Line-scoped Unknown share: 35.7%
- Top Unknown reasons: opaque 92, timeout 57, abstraction 14, unaligned-loop 4, recursion 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 228, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): operation 332, runtime-rule 221, callee 78, symbol 5, type 2, signature 1, constant 1. Of the 221 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 211 EQ007, 10 EQ006, 0 EQ002 and 0 Unknown ().
- `unbound` Unknowns: 0
- Top abstractions: delegate 25, `System.String::Concat(string,string)` 6, `get:System.String::get_Length()` 5, `System.String::StartsWith(string,System.StringComparison)` 5, `System.String::Substring(int)` 4, `op:Duplicati.Library.Utility.TempFile::op_Implicit(Duplicati.Library.Utility.TempFile)` 4, `System.String::IsNullOrEmpty(string)` 2, `{Input}::.ctor(CoCoL.IReadChannel<global::Duplicati.Library.Main.Operation.Backup.IUploadRequest>)` 2, `System.String::IsNullOrWhiteSpace(string)` 2, `System.String::Trim()` 2, `op:Amazon.Runtime.ConstantClass::op_Equality(Amazon.Runtime.ConstantClass,Amazon.Runtime.ConstantClass)` 2, `System.String::Replace(char,char)` 2, `System.Int64::ToString()` 2, `System.String::Replace(string,string)` 2, opaque Conversion 2
- Review list: 98 groups for 422 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 6.7%. Top five: `EQ003 timeout`: 57, `EQ007 runtime-change:System.IO.Path::Combine(string,string)`: 36, `EQ007 runtime-change:System.IO.File::OpenRead(string)`: 22, `EQ003 opaque:CaughtException+rethrow`: 20, `EQ007 runtime-change:extern`: 19
- Replay (`properties.replay`, ADR 0035 decision 2), EQ002 13: not-constructible 11, reproduced 2
- Replay (`properties.replay`, ADR 0035 decision 2), EQ006 24: not-constructible 14, reproduced 10
- Replay (`properties.replay`, ADR 0035 decision 2), EQ007 211: none 130, not-applicable 8, not-constructible 73
- Why a replay is not constructible (EQ002, EQ006 and witnessed EQ007): the divergence is in the call trace 46, the call traces differ before the outcome 27, a side gave no comparable outcome 8, a framework or third-party object with no public parameterless constructor, or with model fields 4, an abstract, interface or generic type no model type names 3, the model constrains field (a static or undeclared field, typeof, caller or thrown) 3, the model constrains thrown (a static or undeclared field, typeof, caller or thrown) 2, the model constrains typeof (a static or undeclared field, typeof, caller or thrown) 1, an argument the driver cannot build 1, not a method, a property getter or a constructor 1, the model constrains field/typeof (a static or undeclared field, typeof, caller or thrown) 1, both sides threw where the model does not 1
- Divergent adjudication: `docs/runs/2026-10-19-execution-reach.md` (P2-245), over `docs/runs/2026-10-17-precision-execute-verdict.md` (P2-237).

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-238 (`b0f56ff8`, no `--execute`): every count is the same but EQ006 14 to 24 and witnessed EQ007 91 to 81: the 10 exposures whose replay is `reproduced` are EQ006 (ADR 0060 decision 4). The solver proves 224, as before.
- Replay: EQ002 13, 2 `reproduced` (`SystemIOWindows::SetAccessControlFile(string,FileSecurity)` and `::SetAccessControlDir(string,DirectorySecurity)`, the first EQ002 replays of the three migrations to reproduce) and 11 `not-constructible`; EQ006 24, 10 `reproduced` and 14 `not-constructible`; witnessed EQ007 81, 8 `not-applicable` and 73 `not-constructible`. No `not-reproduced`.
- `MSSQLDB::GetHashCode()` is `reproduced` on an `uninitialized` receiver its public constructor reaches. `SystemIOLinux::PathGetFullPath(string)` and `Utility.Uri::UriBuilder(string,string,NameValueCollection)` differ only in the two drivers' working directories: P2-254 (new).
- P2-237's 19 uncallable or unbuilt-argument sample rows re-adjudicated: all 19 callable (4 not run: a live service, a process, update folders), 10 confirmed, 1 false positive, 8 still undetermined; precision over the sampled results still Divergent 64.5% (20 of 31), from 50.0% (10 of 20).
- A driver does not await a returned `Task`, so rows 14 and 47 (`AzureBlobWrapper::AddFileStream`, `SharePointBackend::uploadFileSlicePerSlice`) stay undetermined: P2-252 (new). An enum argument is never built from the model (`CompressionHint`): P2-251 (new).
