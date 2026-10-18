# full run: gitextensions-8522

- Pair: human, gitextensions/gitextensions PR #8522 (.NET Framework 4.8 to .NET 5), legacy 3f4ed21998af, modern 5190ba5c1a5f
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: b0f56ff8, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 334s (5m34s), exit 1. Exit 1 means Divergent results exist (here a witnessed runtime exposure counts as one, ADR 0060 decision 5). No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-238: the five large pairs one after the other from one Release build, on the checkouts P2-212 fetched and restored, nothing else started from this session while they ran. The last summary of this pair is `docs/runs/2026-10-17-full-execute-gitextensions-8522/SUMMARY.md`; `docs/runs/2026-10-17-runtime-exposure-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 48 | 0.001 | +0.001 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.514 | n/a |
| match | 1 | 0.019 | n/a |
| lower | 13592 | 110.297 | +54.555 |
| verify | 13592 | 189.515 | -50.808 |
| write | 1 | 0.309 | +0.000 |

## Load
- Projects: legacy 48 with a detected runtime, modern 43; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Runtimes detected (`run.properties.runtimes`): legacy net461 (48) [source: attribute 47, host 1]; modern net5.0 (43) [source: attribute 42, host 1]

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
| DelegateCreation | 178 | 175 | none open (P2-182 done) |
| rebound-call | 144 | 144 | none open (P2-179 done) |
| CaughtException | 109 | 108 | none open (P2-180 done) |
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
| "rebound-call" | 121 | none open (P2-179 done) |
| "no-body" | 51 | P2-214 |
| "LocalFunction" | 34 | P2-178 (one form) |
| "CaughtException" | 18 | none open (P2-180 done) |
| "DelegateCreation" | 11 | none open (P2-182 done) |
| "Tuple" | 10 | none |
| "Conversion" | 9 | none open (P2-123 done) |
| "iterator" | 8 | none |
| "Binary" | 7 | none open (P2-087 done) |
| "ArrayElementReference" | 5 | P2-183 (OpenRA forms) |
| "InterpolatedString" | 5 | P2-102 (one form; P2-139 counted the rest) |
| "call-throw-in-try" | 5 | none |
| "CaughtException+rebound-call" | 4 | none open (P2-180 done); none open (P2-179 done) |
| "CompoundAssignment" | 4 | none open (P2-141 done) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 918 | 918 |
| distinct members | 99 | 96 |
| pairs with any | 526 | 527 |

- Package changes: 38 version changed, 10 legacy only, 100 modern only; `adystech.credentialmanager 1.7.0 -> 2.1.1`, `appinsights.windowsdesktop 2.10.42-preview -> 2.13.1`, `ben.demystifier 0.1.4 -> 0.1.6`, `castle.core 4.2.0 -> 4.4.0`, `excss 2.0.6 -> 4.1.0`, `fluentassertions 5.2.0 -> 5.10.3`, `jetbrains.annotations 2018.2.1 -> 2020.1.0`, `libgit2sharp 0.25.0 -> 0.26.2`, `libgit2sharp.nativebinaries 1.0.210 -> 2.0.306`, `microsoft-windowsapicodepack-core 1.1.3.3 -> 1.1.4`, `microsoft-windowsapicodepack-shell 1.1.3.3 -> 1.1.4`, `microsoft.applicationinsights 2.10.0-beta4 -> 2.13.1`, `microsoft.codecoverage 15.9.0 -> 16.6.1`, `microsoft.net.test.sdk 15.9.0 -> 16.6.1`, `microsoft.netcore.platforms 1.1.0 -> 1.1.0, 3.0.0, 3.1.0` (first 15 of 38)

## Verdicts (full and seeded only)
- By rule: EQ001 12910, EQ002 24, EQ003 225, EQ004 15, EQ005 192, EQ006 10, EQ007 442 (witnessed 111, not 331)
- Runtime exposures (EQ007, ADR 0060): an unedited body a runtime change reaches. Witnessed ones carry `properties.model` and count as Divergent for the exit code; the others carry `unknownReason` and count as Unknown. Divergent is EQ002 + EQ006 alone, and Unknown EQ003 alone, so the three never overlap.
- By proofMethod, Equivalent results: congruence 12755, bounded 136, lockstep-induction 19
- Changed pairs by outcome: proved Equivalent by the solver 155 (18.5%), Unknown 206 (24.6%), Divergent 34 (4.1%; EQ002 24, EQ006 10), runtime exposure 442 (52.8%; witnessed 111, not 331). `unmatched-overload` Unknowns, not matched pairs: 19
- Unknown by scope (EQ003): line 120, method 86. Line-scoped Unknown share: 58.3%
- Top Unknown reasons (EQ003): opaque 134, abstraction 44, timeout 22, unaligned-loop 5, recursion 1
- Runtime exposures without a witness, by `unknownReason`: abstraction 162, opaque 97, timeout 67, unaligned-loop 5; by first rule (up to 8): `System.String::StartsWith(` 64, `extern` 51, `System.IO.Path::Combine(string,string)` 29, `System.String::EndsWith(` 25, `System.String::IndexOf(` 18, `System.IO.File::Delete(string)` 15, `System.IO.Path::GetDirectoryName(string)` 13, `System.String::Equals(string,System.StringComparison)` 10
- Runtime exposures with a witness, by first rule (up to 8): `System.IO.Path::Combine(string,string)` 22, `System.String::StartsWith(` 13, `System.String::Equals(string,System.StringComparison)` 6, `System.String::GetHashCode(` 5, `System.Diagnostics.Process::Start(` 5, `System.Text.Encoding::get_Default(` 5, `System.Char::IsLetterOrDigit(char)` 4, `System.Text.Encoding::GetString(` 4
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 152, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 443, callee 163, operation 110, type-layout 99, symbol 11, type 7, constant 3, signature 1. Of the 443 `runtime-rule` pairs, whose two texts are equal, 1 are proved, 0 EQ002 and 442 EQ007; none is EQ006 or EQ003, as ADR 0060 decision 2 has it.
- `unbound` Unknowns: 0
- Top abstractions, over every `abstraction` result (EQ003 and unwitnessed EQ007): delegate 222, `System.String::Concat(string,string)` 80, `get:System.String::get_Length()` 63, `System.String::IsNullOrEmpty(string)` 62, `System.String::IsNullOrWhiteSpace(string)` 50, opaque Conversion 37, `System.String::Substring(int)` 29, `op:System.String::op_Equality(string,string)` 25, `System.String::Substring(int,int)` 24, `System.String::Trim()` 20, `op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder)` 18, opaque InterpolatedString 14, `System.String::get_Chars(int)` 13, opaque LocalFunction 13, `System.String::TrimEnd(char)` 12
- Review list: 116 groups for 701 flagged results (EQ002 + EQ003 + EQ006 + EQ007), from `run.properties.reviewList`; flagged results as a share of matched pairs: 5.2%. Top five: `EQ003 opaque:rebound-call`: 108, `EQ007 runtime-change:System.String::StartsWith(`: 77, `EQ007 runtime-change:System.IO.Path::Combine(string,string)`: 51, `EQ007 runtime-change:extern`: 51, `EQ003 abstraction`: 38
- Divergent adjudication: `docs/runs/2026-10-17-runtime-exposure-verdict.md` (P2-212's sample, with P2-237's execution, split by these runs' rule ids).

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-237 (`de93b9c7`): its 111 EQ006 and 331 Unknown on `runtime-rule` pairs are the 111 witnessed and 331 unwitnessed EQ007, exactly; EQ006 121 to 10, the ten whose texts differ. EQ002 24 and the one proof on a `runtime-rule` pair are unchanged; no `runtime-rule` pair is EQ002, so ADR 0060 decision 2's soundness case does not occur. The solver proves 155, from 154 (P2-179). ADR 0060 projected 475 exposures from P2-212's run; P2-212 to P2-237 cleared 33 `runtime-rule` pairs (the path and row tickets P2-207, P2-208, P2-216 to P2-218, P2-220, P2-228, P2-231 and P2-232), so 442.
- The unwitnessed exposures are led by `String.StartsWith(string)` 64 and `extern` 51 (the `[DllImport]` bodies, P2-214); the witnessed by `Path.Combine(string,string)` 22.
