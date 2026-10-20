# full run: duplicati-3124

- Pair: human, duplicati/duplicati PR #3124 (.NET Framework 4.7.1 to .NET 5, first step), legacy beaf03562fdc, modern 4f565a004f52
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: 4b2e0df6, mode full with `--execute`, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, committed config `tools/corpus/configs/duplicati-3124.json` (P2-213), wall-clock 598s (9m58s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-247: the five large pairs one after the other from one Release build of `main`, on the checkouts and restores P2-237 made, nothing else started on this machine while they ran, with a private `dotnet` root holding the runtimes the pairs need (`docs/runs/2026-10-20-precision-execute-verdict-2.md`). The last summary of this pair is `docs/runs/2026-10-19-full-execute-duplicati-3124/SUMMARY.md`; the verdict sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 51 | 0.001 | +0.001 |
| load-modern | 52 | 0.000 | +0.000 |
| enumerate | 2 | 0.242 | n/a |
| match | 1 | 0.009 | n/a |
| lower | 6294 | 42.668 | +7.283 |
| verify | 6294 | 499.198 | -255.250 |
| execute | 6294 | 26.281 | -8.069 |
| write | 1 | 0.194 | +0.000 |

## Load
- Projects: legacy 51 with a detected runtime, modern 52; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Runtimes detected (`run.properties.runtimes`): legacy net471 (51) [source: attribute 51]; modern net5.0 (52) [source: attribute 52]
- Execution: legacy on the .NET Framework 4.8 CLR (an `app.config` naming each project's net471 sku), modern on `Microsoft.NETCore.App` 5.0.17 (`rollForward: Disable`); a project that uses the Windows Desktop framework runs on `Microsoft.WindowsDesktop.App` 5.0.17 (P2-253), from the private root.

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

- Package changes: 16 version changed, 9 legacy only, 145 modern only; `alphavss 1.4.0 -> 2.0.0`, `fluentftp 27.1.1 -> 28.0.5`, `mailkit 2.3.1.6 -> 2.4.1`, `microsoft.rest.clientruntime.azure 3.3.19 -> 3.3.18`, `mimekit 2.3.1 -> 2.4.1`, `minio 3.1.7 -> 3.1.8`, `newtonsoft.json 12.0.2 -> 12.0.3`, `ngettext 0.6.4 -> 0.6.5`, `sharpaescrypt.dll 1.3.3 -> 1.3.4`, `ssh.net 2020.0.1 -> 2016.1.0, 2020.0.1`, `system.reactive 4.0.0 -> 4.1.2, 4.3.2`, `system.reactive.linq 4.0.0 -> 4.3.2` (first 15 of 16)

## Verdicts (full and seeded only)
- By rule: EQ001 5878, EQ002 14, EQ003 173, EQ004 121, EQ005 65, EQ006 22, EQ007 213 (witnessed 83, not 130)
- Runtime exposures (EQ007, ADR 0060): an unedited body a runtime change reaches. Witnessed ones carry `properties.model` and count as Divergent for the exit code; the others carry `unknownReason` and count as Unknown. Divergent is EQ002 + EQ006 alone, and Unknown EQ003 alone, so the three never overlap.
- By proofMethod, Equivalent results: congruence 5654, bounded 186, lockstep-induction 38
- Changed pairs by outcome: proved Equivalent by the solver 224 (35.0%), Unknown 167 (26.1%), Divergent 36 (5.6%; EQ002 14, EQ006 22), runtime exposure 213 (33.3%; witnessed 83, not 130). An exposure whose replay is `reproduced` is EQ006 (ADR 0060 decision 4): 8 of the EQ006 here. `unmatched-overload` Unknowns, not matched pairs: 6
- Unknown by scope: line 60, method 107. Line-scoped Unknown share: 35.9%
- Top Unknown reasons: opaque 93, timeout 56, abstraction 13, unaligned-loop 4, recursion 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 227, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): operation 332, runtime-rule 221, callee 78, symbol 5, type 2, signature 1, constant 1. Of the 221 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 213 EQ007, 8 EQ006, 0 EQ002 and 0 Unknown ().
- `unbound` Unknowns: 0
- Top abstractions: delegate 25, `System.String::Concat(string,string)` 6, `get:System.String::get_Length()` 5, `System.String::StartsWith(string,System.StringComparison)` 5, `System.String::Substring(int)` 4, `op:Duplicati.Library.Utility.TempFile::op_Implicit(Duplicati.Library.Utility.TempFile)` 4, `System.String::IsNullOrEmpty(string)` 2, `{Input}::.ctor(CoCoL.IReadChannel<global::Duplicati.Library.Main.Operation.Backup.IUploadRequest>)` 2, `System.String::IsNullOrWhiteSpace(string)` 2, `System.String::Trim()` 2, `op:Amazon.Runtime.ConstantClass::op_Equality(Amazon.Runtime.ConstantClass,Amazon.Runtime.ConstantClass)` 2, `System.String::Replace(char,char)` 2, `System.Int64::ToString()` 2, `System.String::Replace(string,string)` 2, opaque Conversion 2
- Review list: 99 groups for 422 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 6.7%. Top five: `EQ003 timeout`: 56, `EQ007 runtime-change:System.IO.Path::Combine(string,string)`: 36, `EQ007 runtime-change:System.IO.File::OpenRead(string)`: 22, `EQ003 opaque:CaughtException+rethrow`: 20, `EQ007 runtime-change:extern`: 19
- Replay (`properties.replay`, ADR 0035 decision 2), EQ002 14: not-constructible 12, reproduced 2
- Replay (`properties.replay`, ADR 0035 decision 2), EQ006 22: not-constructible 14, reproduced 8
- Replay (`properties.replay`, ADR 0035 decision 2), EQ007 213: none 130, not-applicable 10, not-constructible 73
- Why a replay is not constructible (EQ002, EQ006 and witnessed EQ007): the divergence is in the call trace 47, the call traces differ before the outcome 28, a side gave no comparable outcome 8, a framework or third-party object with no public parameterless constructor, or with model fields 4, an abstract, interface or generic type no model type names 3, the model constrains field (a static or undeclared field, typeof, caller or thrown) 2, the model constrains thrown (a static or undeclared field, typeof, caller or thrown) 2, the model constrains typeof (a static or undeclared field, typeof, caller or thrown) 1, an argument the driver cannot build 1, not a method, a property getter or a constructor 1, the model constrains field/typeof (a static or undeclared field, typeof, caller or thrown) 1, both sides threw where the model does not 1
- Divergent and exposure adjudication: `docs/runs/2026-10-20-precision-execute-verdict-2.md` (P2-247).

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-238 (`b0f56ff8`, no `--execute`): EQ002 13 to 14, EQ003 168 to 167; 8 EQ006 with `proofMethod: observed` are witnessed exposures whose replay is `reproduced` (witnessed EQ007 91 to 83).
- Replay: no `not-reproduced`. 10 `reproduced`: `MSSQLDB::GetHashCode()` shows the runtime change; 9 `SystemIOWindows` members (EQ002 `SetAccessControlFile`, `SetAccessControlDir`; EQ006 `DirectoryCreate`, `DirectoryDelete`, `FileDelete`, `FileCreate`, `GetFileAttributes`, `PathGetFileName`, `PathGetFullPath`) differ only because the legacy driver runs .NET Framework's legacy path handling, which rejects every `\\?\` path the solution's own hosts accept (P2-259, new).
- Adjudicated: EQ002 and EQ006 12 confirmed, 9 false positives, 7 undetermined, precision 57.1% (12 of 21); observed EQ006 1 confirmed, 6 false positives, 1 undetermined; of 30 sampled exposures 12 confirmed, 13 false positives, 5 undetermined. P2-237's confirmation of `SystemIOWindows::GetFileAttributes(string)` rested on P2-259's quirk and is a false positive now (P2-260).
- False-positive causes filed by P2-247: P2-260, P2-261, P2-262, P2-263, P2-264, P2-265, P2-269, P2-271.
