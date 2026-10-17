# full run: gitextensions-8522

- Pair: human, gitextensions/gitextensions PR #8522 (.NET Framework 4.8 to .NET 5), legacy 3f4ed21998af, modern 5190ba5c1a5f
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: de93b9c7, mode full with `--execute`, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 488s (8m08s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-237: the three migration pairs one after the other from one build, nothing else started from this session while they ran, with the .NET 5.0.17 runtime installed for these runs. The last summary of this pair is `docs/runs/2026-10-15-full-gitextensions-8522/SUMMARY.md`; `docs/runs/2026-10-17-precision-execute-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 48 | 0.001 | +0.001 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.758 | n/a |
| match | 1 | 0.030 | n/a |
| lower | 13592 | 141.310 | +69.355 |
| verify | 13592 | 287.805 | -108.348 |
| execute | 13592 | 8.966 | +3.671 |
| write | 1 | 0.378 | +0.000 |

## Load
- Projects: legacy 48 with a detected runtime, modern 43; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy Setup; modern Setup
- Runtimes detected (`run.properties.runtimes`): legacy net461 (48) [source: attribute 47, host 1]; modern net5.0 (43) [source: attribute 42, host 1]
- Execution: legacy on the .NET Framework 4.8 CLR (an `app.config` naming each project's net461 sku), modern on `Microsoft.NETCore.App` 5.0.17 (`rollForward: Disable`). Replay drivers run on `Microsoft.NETCore.App` only, so no `Microsoft.WindowsDesktop.App` 5.0 was needed or installed.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 13784 | 13607 |
| analysed lines | 186633 | 184691 |

- Matched pairs 13592; without opaque 11653 (85.7%); whole-body opaque 124 (0.9%); congruent 12755 (93.8%)
- Unchanged share: 93.8% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 1377 of 1577 legacy `.cs` files (184406 of 249359 lines, 74.0%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 93.8%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| InstanceReference | 364 | 364 | none |
| Binary | 234 | 234 | none open (P2-087 done) |
| Conversion | 233 | 232 | none open (P2-123 done) |
| DelegateCreation | 205 | 202 | P2-182 (OpenRA forms; P2-136 done) |
| LocalFunction | 203 | 203 | P2-178 (one form) |
| rebound-call | 144 | 144 | P2-179 |
| CaughtException | 114 | 113 | P2-180 |
| Layout | 108 | 108 | none |
| switch-pattern | 103 | 103 | none open (P2-122 done) |
| iterator | 73 | 73 | none |
| ArrayElementReference | 63 | 63 | P2-183 (OpenRA forms) |
| InterpolatedString | 59 | 59 | P2-102 (one form; P2-139 counted the rest) |
| DefaultValue | 52 | 52 | none open (P2-138 done) |
| no-body | 51 | 51 | P2-214 |
| Tuple | 48 | 48 | none |

## Changed code
- Changed pairs 837 of 13592; without opaque 463; whole-body opaque 59
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 55.3%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 463 | n/a |
| "rebound-call" | 121 | P2-179 |
| "no-body" | 51 | P2-214 |
| "LocalFunction" | 34 | P2-178 (one form) |
| "CaughtException" | 18 | P2-180 |
| "DelegateCreation" | 12 | P2-182 (OpenRA forms; P2-136 done) |
| "Tuple" | 10 | none |
| "Conversion" | 9 | none open (P2-123 done) |
| "iterator" | 8 | none |
| "Binary" | 7 | none open (P2-087 done) |
| "ArrayElementReference" | 5 | P2-183 (OpenRA forms) |
| "call-throw-in-try" | 5 | none |
| "CaughtException+rebound-call" | 4 | P2-180; P2-179 |
| "CompoundAssignment" | 4 | none open (P2-141 done) |
| "InterpolatedString" | 4 | P2-102 (one form; P2-139 counted the rest) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 918 | 918 |
| distinct members | 99 | 96 |
| pairs with any | 526 | 527 |

- Package changes: 38 version changed, 10 legacy only, 100 modern only; `adystech.credentialmanager 1.7.0 -> 2.1.1`, `appinsights.windowsdesktop 2.10.42-preview -> 2.13.1`, `ben.demystifier 0.1.4 -> 0.1.6`, `castle.core 4.2.0 -> 4.4.0`, `excss 2.0.6 -> 4.1.0`, `fluentassertions 5.2.0 -> 5.10.3`, `jetbrains.annotations 2018.2.1 -> 2020.1.0`, `libgit2sharp 0.25.0 -> 0.26.2`, `libgit2sharp.nativebinaries 1.0.210 -> 2.0.306`, `microsoft-windowsapicodepack-core 1.1.3.3 -> 1.1.4`, `microsoft-windowsapicodepack-shell 1.1.3.3 -> 1.1.4`, `microsoft.applicationinsights 2.10.0-beta4 -> 2.13.1`, `microsoft.codecoverage 15.9.0 -> 16.6.1`, `microsoft.net.test.sdk 15.9.0 -> 16.6.1`, `microsoft.netcore.platforms 1.1.0 -> 1.1.0, 3.0.0, 3.1.0` (first 15 of 38; unchanged since P2-212)

## Verdicts (full and seeded only)
- By rule: EQ001 12909, EQ002 24, EQ003 557, EQ004 15, EQ005 192, EQ006 121
- By proofMethod, Equivalent results: congruence 12755, bounded 136, lockstep-induction 18
- Changed pairs by outcome: proved Equivalent by the solver 154 (18.4%), Unknown 538 (64.3%), Divergent 145 (17.3%; EQ002 24, EQ006 121). `unmatched-overload` Unknowns, not matched pairs: 19
- Unknown by scope: line 151, method 387. Line-scoped Unknown share: 28.1%
- Top Unknown reasons: opaque 231, abstraction 206, timeout 90, unaligned-loop 10, recursion 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 153, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 443, callee 163, operation 110, type-layout 99, symbol 11, type 7, constant 3, signature 1. Of the 443 `runtime-rule` pairs, whose two texts are equal, 1 are proved, 111 are EQ006, 0 EQ002 and 331 Unknown (abstraction 162, opaque 97, timeout 67, unaligned-loop 5).
- `unbound` Unknowns: 0
- Top abstractions: delegate 222, `System.String::Concat(string,string)` 80, `get:System.String::get_Length()` 62, `System.String::IsNullOrEmpty(string)` 62, `System.String::IsNullOrWhiteSpace(string)` 50, opaque Conversion 37, `System.String::Substring(int)` 29, `op:System.String::op_Equality(string,string)` 25, `System.String::Substring(int,int)` 22, `System.String::Trim()` 20, `op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder)` 18, opaque InterpolatedString 14, `System.String::get_Chars(int)` 13, opaque LocalFunction 13, `System.String::TrimEnd(char)` 12
- Review list: 109 groups for 702 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 5.2%. Top five: `EQ003 abstraction`: 173, `EQ003 opaque:rebound-call`: 107, `EQ003 timeout`: 90, `EQ003 opaque:no-body`: 51, `EQ006 runtime-change:System.IO.Path::Combine(string,string)`: 26
- Replay (`properties.replay`, ADR 0035 decision 2), EQ002 24: not-constructible 24
- Replay (`properties.replay`, ADR 0035 decision 2), EQ006 121: not-applicable 4, not-constructible 115, reproduced 2
- Why a replay is not constructible: the divergence is in the call trace 48, not public 38, the model constrains a field, cast or array 23, no public parameterless constructor on the receiver type 17, an argument the driver cannot build 5, the call traces differ before the outcome 4, not a method or a property getter 1, a by-reference parameter 1, a side gave no comparable outcome 1, both sides threw where the model does not 1
- Divergent adjudication: `docs/runs/2026-10-17-precision-execute-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-212 (`bfbf413e`): EQ006 137 to 121, EQ002 23 to 24, congruent 12,722 to 12,755; the solver proves 154 changed pairs, as before.
- Replay: every EQ002 (24) is `not-constructible`; of 121 EQ006, 2 `reproduced`, 4 `not-applicable`, 115 `not-constructible`. No `not-reproduced`, so no soundness or modelling ticket (ADR 0035 decision 2).
- P2-212's sample executed (53 rows, 35 undetermined): 4 confirmed by `probe` (`LocalHook::GetProcAddress(string,string)`, `SpellCheckerHelper::IsSeparator(char)`, `ScriptOptionsParser::DependsOnSelectedRevision(string)`, `PathUtil::Combine(string,string)`), 1 false positive (`FileSettingsCacheTests::ctor_FileWatcher_Filter_should_be_default_if_invalid_dir(string)`: P2-243, new), 30 still undetermined; precision over the results still Divergent here 26.3% (5 of 19).
- A replay or probe driver never calls a constructor, passes a struct or compares a struct or `IntPtr` result: P2-242 (new).
