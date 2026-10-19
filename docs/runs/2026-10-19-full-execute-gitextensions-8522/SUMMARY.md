# full run: gitextensions-8522

- Pair: human, gitextensions/gitextensions PR #8522 (.NET Framework 4.8 to .NET 5), legacy 3f4ed21998af, modern 5190ba5c1a5f
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: bceeea62, mode full with `--execute`, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 338s (5m38s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-245: the three migration pairs one after the other from one Release build, on the checkouts and restores P2-237 made, nothing else started from this session while they ran. The last summary of this pair is `docs/runs/2026-10-17-full-gitextensions-8522/SUMMARY.md`; `docs/runs/2026-10-19-execution-reach.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 48 | 0.001 | +0.001 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.530 | n/a |
| match | 1 | 0.019 | n/a |
| lower | 13592 | 108.090 | +54.495 |
| verify | 13592 | 179.808 | -46.627 |
| execute | 13592 | 14.966 | +0.896 |
| write | 1 | 0.249 | +0.000 |

## Load
- Projects: legacy 48 with a detected runtime, modern 43; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy Setup; modern Setup
- Runtimes detected (`run.properties.runtimes`): legacy net461 (48) [source: attribute 47, host 1]; modern net5.0 (43) [source: attribute 42, host 1]
- Execution: legacy on the .NET Framework 4.8 CLR (an `app.config` naming each project's net461 sku), modern on `Microsoft.NETCore.App` 5.0.17 (`rollForward: Disable`). Replay and `probe` drivers run on `Microsoft.NETCore.App` only, and this host has no `Microsoft.WindowsDesktop.App` 5.0, so a modern member that loads a Windows Forms type is not constructible there (P2-253).

## Census
| | legacy | modern |
|---|---|---|
| procedures | 13784 | 13607 |
| analysed lines | 186633 | 184691 |

- Matched pairs 13592; without opaque 11674 (85.9%); whole-body opaque 124 (0.9%); congruent 12755 (93.8%)
- Unchanged share: 93.8% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 1377 of 1577 legacy `.cs` files (184406 of 249359 lines, 74.0%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 93.8%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| InstanceReference | 364 | 364 | none |
| Binary | 234 | 234 | none open (P2-087 done) |
| Conversion | 233 | 232 | none open (P2-123 done) |
| LocalFunction | 203 | 203 | P2-178 (one form) |
| DelegateCreation | 178 | 175 | P2-182 (OpenRA forms; P2-136 done) |
| rebound-call | 144 | 144 | P2-179 |
| CaughtException | 109 | 108 | P2-180 |
| Layout | 108 | 108 | none |
| switch-pattern | 103 | 103 | none open (P2-122 done) |
| iterator | 73 | 73 | none |
| ArrayElementReference | 63 | 63 | P2-183 (OpenRA forms) |
| InterpolatedString | 59 | 59 | P2-102 (one form; P2-139 counted the rest) |
| DefaultValue | 52 | 52 | none open (P2-138 done) |
| no-body | 51 | 51 | P2-214 |
| Tuple | 48 | 48 | none |

## Changed code
- Changed pairs 837 of 13592; without opaque 466; whole-body opaque 59
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 55.7%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 466 | n/a |
| "rebound-call" | 121 | P2-179 |
| "no-body" | 51 | P2-214 |
| "LocalFunction" | 34 | P2-178 (one form) |
| "CaughtException" | 18 | P2-180 |
| "DelegateCreation" | 11 | P2-182 (OpenRA forms; P2-136 done) |
| "Tuple" | 10 | none |
| "Conversion" | 9 | none open (P2-123 done) |
| "iterator" | 8 | none |
| "Binary" | 7 | none open (P2-087 done) |
| "ArrayElementReference" | 5 | P2-183 (OpenRA forms) |
| "InterpolatedString" | 5 | P2-102 (one form; P2-139 counted the rest) |
| "call-throw-in-try" | 5 | none |
| "CaughtException+rebound-call" | 4 | P2-180; P2-179 |
| "CompoundAssignment" | 4 | none open (P2-141 done) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 918 | 918 |
| distinct members | 99 | 96 |
| pairs with any | 526 | 527 |

- Package changes: 38 version changed, 10 legacy only, 100 modern only; `adystech.credentialmanager 1.7.0 -> 2.1.1`, `appinsights.windowsdesktop 2.10.42-preview -> 2.13.1`, `ben.demystifier 0.1.4 -> 0.1.6`, `castle.core 4.2.0 -> 4.4.0`, `excss 2.0.6 -> 4.1.0`, `fluentassertions 5.2.0 -> 5.10.3`, `jetbrains.annotations 2018.2.1 -> 2020.1.0`, `libgit2sharp 0.25.0 -> 0.26.2`, `libgit2sharp.nativebinaries 1.0.210 -> 2.0.306`, `microsoft-windowsapicodepack-core 1.1.3.3 -> 1.1.4`, `microsoft-windowsapicodepack-shell 1.1.3.3 -> 1.1.4`, `microsoft.applicationinsights 2.10.0-beta4 -> 2.13.1`, `microsoft.codecoverage 15.9.0 -> 16.6.1`, `microsoft.net.test.sdk 15.9.0 -> 16.6.1`, `microsoft.netcore.platforms 1.1.0 -> 1.1.0, 3.0.0, 3.1.0` (first 15 of 38; unchanged since P2-212)

## Verdicts (full and seeded only)
- By rule: EQ001 12910, EQ002 24, EQ003 225, EQ004 15, EQ005 192, EQ006 18, EQ007 434 (witnessed 103, not 331)
- Runtime exposures (EQ007, ADR 0060): an unedited body a runtime change reaches. Witnessed ones carry `properties.model` and count as Divergent for the exit code; the others carry `unknownReason` and count as Unknown. Divergent is EQ002 + EQ006 alone, and Unknown EQ003 alone, so the three never overlap.
- By proofMethod, Equivalent results: congruence 12755, bounded 136, lockstep-induction 19
- Changed pairs by outcome: proved Equivalent by the solver 155 (18.5%), Unknown 206 (24.6%), Divergent 42 (5.0%; EQ002 24, EQ006 18), runtime exposure 434 (51.9%; witnessed 103, not 331). An exposure whose replay is `reproduced` is EQ006 (ADR 0060 decision 4): 8 of the EQ006 here. `unmatched-overload` Unknowns, not matched pairs: 19
- Unknown by scope: line 120, method 86. Line-scoped Unknown share: 58.3%
- Top Unknown reasons: opaque 134, abstraction 44, timeout 22, unaligned-loop 5, recursion 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 152, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 443, callee 163, operation 110, type-layout 99, symbol 11, type 7, constant 3, signature 1. Of the 443 `runtime-rule` pairs, whose two texts are equal, 1 are proved, 434 EQ007, 8 EQ006, 0 EQ002 and 0 Unknown ().
- `unbound` Unknowns: 0
- Top abstractions: delegate 37, `get:System.String::get_Length()` 17, `System.String::TrimEnd(char)` 12, `System.String::IsNullOrEmpty(string)` 12, `op:System.String::op_Equality(string,string)` 10, `System.String::Concat(string,string)` 8, `System.String::Substring(int)` 7, `System.String::IsNullOrWhiteSpace(string)` 6, opaque Tuple 6, `System.String::Substring(int,int)` 6, `System.String::StartsWith(string,System.StringComparison)` 2, opaque InterpolatedString 2, `System.Int32::ToString()` 2, `System.String::get_Chars(int)` 2, `System.Char::IsWhiteSpace(char)` 2
- Review list: 118 groups for 701 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 5.2%. Top five: `EQ003 opaque:rebound-call`: 108, `EQ007 runtime-change:System.String::StartsWith(`: 77, `EQ007 runtime-change:extern`: 51, `EQ007 runtime-change:System.IO.Path::Combine(string,string)`: 49, `EQ003 abstraction`: 38
- Replay (`properties.replay`, ADR 0035 decision 2), EQ002 24: not-constructible 24
- Replay (`properties.replay`, ADR 0035 decision 2), EQ006 18: not-constructible 10, reproduced 8
- Replay (`properties.replay`, ADR 0035 decision 2), EQ007 434: none 331, not-applicable 10, not-constructible 93
- Why a replay is not constructible (EQ002, EQ006 and witnessed EQ007): the divergence is in the call trace 48, the call traces differ before the outcome 18, the model constrains field (a static or undeclared field, typeof, caller or thrown) 17, a framework or third-party object with no public parameterless constructor, or with model fields 8, an abstract, interface or generic type no model type names 7, a window or the desktop session 7, a side gave no comparable outcome 7, an argument the driver cannot build 5, both sides threw where the model does not 3, the model constrains typeof (a static or undeclared field, typeof, caller or thrown) 2, the model constrains thrown (a static or undeclared field, typeof, caller or thrown) 2, not a method, a property getter or a constructor 1, a by-reference parameter 1, the model constrains field/typeof (a static or undeclared field, typeof, caller or thrown) 1
- Divergent adjudication: `docs/runs/2026-10-19-execution-reach.md` (P2-245), over `docs/runs/2026-10-17-precision-execute-verdict.md` (P2-237).

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-238 (`b0f56ff8`, no `--execute`): every count is the same but EQ006 10 to 18 and witnessed EQ007 111 to 103: the 8 exposures whose replay is `reproduced` are EQ006 (ADR 0060 decision 4). The solver proves 155, as before.
- Replay: EQ002 24, all `not-constructible`; EQ006 18, 8 `reproduced` and 10 `not-constructible`; witnessed EQ007 103, 10 `not-applicable` and 93 `not-constructible`. No `not-reproduced`. No reason is "not public" or "no public parameterless constructor" any more.
- Of the 8 `reproduced`, 4 rest on an `uninitialized` receiver a public constructor or the solution's own initializer reaches (`AutoCompleteWord`, `ExternalLink`, `Git.hub.Organization` and `User` `GetHashCode()`). `GitModule::GetSubmoduleFullPath(string)` differs only in the two drivers' working directories: P2-254 (new). `AppSettings::GetDictionaryDir()`, `::GetGitExtensionsDirectory()` and `::ReadBoolRegKey(string,bool)` differ because the modern `AppSettings` static constructor cannot load `System.Windows.Forms` in a replay driver: P2-253 (new).
- P2-237's 24 uncallable or unbuilt-argument sample rows re-adjudicated: 19 callable, 6 confirmed, 6 false positives, 12 still undetermined; precision over the sampled results still Divergent 35.5% (11 of 31), from 26.3% (5 of 19).
- Four false EQ002 (EasyHook rows 0, 3, 7, 18) rest on `IntPtr.Zero` read as a free input on one side: P2-250 (new).
