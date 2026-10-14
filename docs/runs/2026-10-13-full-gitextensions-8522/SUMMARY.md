# full run: gitextensions-8522

- Pair: human, gitextensions/gitextensions PR #8522 (.NET Framework 4.8 to .NET 5), legacy 3f4ed21998af, modern 5190ba5c1a5f
- Corpus list: `tools/corpus/pairs.csv`
- Migrated by: human (upstream PR #8522)
- equiv: e98fd1b9, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 413s (6m53s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: no `run.properties.unverified`, 0 tool execution notifications. The box ran nothing else. The last summary of this pair is `docs/runs/2026-10-08-full-gitextensions-8522/SUMMARY.md` (equiv 18b751f7, same compare mode and `--jobs`); `docs/runs/2026-10-14-migrations-verdict.md` sets the two side by side.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 48 | 0.001 | +0.001 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.524 | n/a |
| match | 1 | 0.032 | n/a |
| lower | 13592 | 90.650 | +46.556 |
| verify | 13592 | 285.605 | -102.586 |
| write | 1 | 0.212 | +0.000 |

## Load
- Projects: legacy 48 with a detected runtime, modern 43; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy Setup; modern Setup
- Runtimes detected (`run.properties.runtimes`): legacy net461 (48); modern net5.0 (43)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 13784 | 13607 |
| analysed lines | 186633 | 184691 |

- Matched pairs 13592; without opaque 11653 (85.7%); whole-body opaque 124 (0.9%); congruent 12654 (93.1%)
- Unchanged share: 93.1% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 1377 of 1577 legacy `.cs` files (184406 of 249359 lines) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 93.1%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| InstanceReference | 364 | 364 | none |
| Binary | 234 | 234 | none open (P2-087 done) |
| Conversion | 233 | 232 | none open (P2-123 done) |
| DelegateCreation | 205 | 202 | none open (P2-136 done) |
| LocalFunction | 203 | 203 | P2-178 (one form) |
| rebound-call | 144 | 144 | P2-179 (new; P2-137 done) |
| CaughtException | 114 | 113 | P2-180 (new; P2-140 done) |
| Layout | 108 | 108 | none |
| switch-pattern | 103 | 103 | none open (P2-122 done) |
| iterator | 73 | 73 | none |
| ArrayElementReference | 63 | 63 | none |
| InterpolatedString | 59 | 59 | P2-102 (one form) |
| DefaultValue | 52 | 52 | none open (P2-138 done) |
| no-body | 51 | 51 | P2-165 |
| Tuple | 48 | 48 | none |

## Changed code
- Changed pairs 938 of 13592; without opaque 535; whole-body opaque 60
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 57.0%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 535 | n/a |
| "rebound-call" | 121 | P2-179 |
| "no-body" | 51 | P2-165 |
| "LocalFunction" | 37 | P2-178 (one form) |
| "DelegateCreation" | 27 | none open (P2-136 done) |
| "CaughtException" | 20 | P2-180 |
| "Tuple" | 10 | none |
| "Conversion" | 9 | none open (P2-123 done) |
| "iterator" | 9 | none |
| "Binary" | 8 | none open (P2-087 done) |
| "ArrayElementReference" | 5 | none |
| "CompoundAssignment" | 5 | none open (P2-141 done) |
| "call-throw-in-try" | 5 | none |
| "CaughtException+rebound-call" | 4 | P2-180; P2-179 |
| "InterpolatedString" | 4 | P2-102 (one form) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 918 | 918 |
| distinct members | 99 | 96 |
| pairs with any | 526 | 527 |

- Package changes: 38 version changed, 10 legacy only, 100 modern only. Version-changed (first 15): adystech.credentialmanager 1.7.0 -> 2.1.1; appinsights.windowsdesktop 2.10.42-preview -> 2.13.1; ben.demystifier 0.1.4 -> 0.1.6; castle.core 4.2.0 -> 4.4.0; excss 2.0.6 -> 4.1.0; fluentassertions 5.2.0 -> 5.10.3; jetbrains.annotations 2018.2.1 -> 2020.1.0; libgit2sharp 0.25.0 -> 0.26.2; libgit2sharp.nativebinaries 1.0.210 -> 2.0.306; microsoft-windowsapicodepack-core 1.1.3.3 -> 1.1.4; microsoft-windowsapicodepack-shell 1.1.3.3 -> 1.1.4; microsoft.applicationinsights 2.10.0-beta4 -> 2.13.1; microsoft.codecoverage 15.9.0 -> 16.6.1; microsoft.net.test.sdk 15.9.0 -> 16.6.1; microsoft.netcore.platforms 1.1.0 -> 1.1.0, 3.0.0, 3.1.0

## Verdicts (full and seeded only)
- By rule: EQ001 12794, EQ002 29, EQ003 622, EQ004 15, EQ005 192, EQ006 166
- By proofMethod, Equivalent results: congruence 12654, bounded 126, lockstep-induction 14
- Changed pairs by outcome: proved Equivalent by the solver 140 (14.9%), Unknown 603 (64.3%; 622 less 19 `unmatched-overload`), Divergent 195 (20.8%; EQ002 29, EQ006 166)
- Unknown by scope: line 154, method 468. Line-scoped Unknown share: 24.8%
- Top Unknown reasons: abstraction 252, opaque 233, timeout 104, unmatched-overload 19, unaligned-loop 13, recursion 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 176, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 493, callee 163, operation 110, type-layout 99, no-fingerprint 51, symbol 11, type 7, constant 3, signature 1. Of the 493 `runtime-rule` pairs, whose two texts are equal, 4 are proved, 150 are EQ006 and 339 Unknown (abstraction 201, timeout 76, opaque 54, unaligned-loop 8).
- `unbound` Unknowns: 0
- Top abstractions: delegate 95, `System.String::Concat(string,string)` 50, `System.String::IsNullOrEmpty(string)` 43, `get:System.String::get_Length()` 41, `System.String::Substring(int)` 29, `System.String::IsNullOrWhiteSpace(string)` 26, `System.String::Substring(int,int)` 24, `System.String::Trim()` 16, `op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder)` 16, `op:System.String::op_Equality(string,string)` 14, `System.String::get_Chars(int)` 9, opaque Conversion 9, `System.String::Replace(string,string)` 8, opaque LocalFunction 7, opaque InterpolatedString 6
- Review list: 119 groups for 817 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 6.0%. Top five: `EQ003 abstraction`: 212, `EQ003 timeout`: 104, `EQ003 opaque:rebound-call`: 103, `EQ003 opaque:no-body`: 51, `EQ006 runtime-change:System.IO.Path::Combine(string,string)`: 35
- Divergent adjudication: `docs/runs/2026-10-14-migrations-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown.
- Unedited bodies a runtime rule keeps out of congruence: 493 of 938 changed pairs, 4 proved: P2-181 (new).
- `rebound-call` alone in 121 of 938 changed pairs (12.9%): P2-179 (new).
- `no-body` alone in 51 (5.4%): P2-165 (existing).
- Divergent sample (59 of 195): 1 confirmed, 28 false positive, 30 undetermined; precision 3.4% (1 of 29). False-positive causes: P2-184, P2-185, P2-186, P2-187, P2-188, P2-189, P2-190 (new) and P2-097 (existing); the verdict's appendix gives each procedure's ticket.
