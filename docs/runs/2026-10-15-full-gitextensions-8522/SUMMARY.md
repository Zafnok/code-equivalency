# full run: gitextensions-8522

- Pair: human, gitextensions/gitextensions PR #8522 (.NET Framework 4.8 to .NET 5), legacy 3f4ed21998af, modern 5190ba5c1a5f
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: bfbf413e, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 363s (6m03s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-212: the five large pairs one after the other from one build, on a box that ran nothing else. The last summary of this pair is `docs/runs/2026-10-13-full-gitextensions-8522/SUMMARY.md`; `docs/runs/2026-10-15-precision-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 48 | 0.001 | +0.001 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.466 | n/a |
| match | 1 | 0.034 | n/a |
| lower | 13592 | 87.714 | +39.970 |
| verify | 13592 | 240.776 | -91.238 |
| write | 1 | 0.218 | +0.000 |

## Load
- Projects: legacy 48 with a detected runtime, modern 43; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy Setup; modern Setup
- Runtimes detected (`run.properties.runtimes`): legacy net461 (48) [source: attribute 47, host 1]; modern net5.0 (43) [source: attribute 42, host 1]

## Census
| | legacy | modern |
|---|---|---|
| procedures | 13784 | 13607 |
| analysed lines | 186633 | 184691 |

- Matched pairs 13592; without opaque 11653 (85.7%); whole-body opaque 124 (0.9%); congruent 12722 (93.6%)
- Unchanged share: 93.6% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 1377 of 1577 legacy `.cs` files (184406 of 249359 lines, 74.0%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 93.6%. Not the row above; ADR 0034.

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
| no-body | 51 | 51 | none open (P2-165 done) |
| Tuple | 48 | 48 | none |

## Changed code
- Changed pairs 870 of 13592; without opaque 480; whole-body opaque 59
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 55.2%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 480 | n/a |
| "rebound-call" | 121 | P2-179 |
| "no-body" | 51 | none open (P2-165 done) |
| "LocalFunction" | 34 | P2-178 (one form) |
| "DelegateCreation" | 25 | P2-182 (OpenRA forms; P2-136 done) |
| "CaughtException" | 19 | P2-180 |
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

- Package changes: 38 version changed, 10 legacy only, 100 modern only; `adystech.credentialmanager 1.7.0 -> 2.1.1`, `ben.demystifier 0.1.4 -> 0.1.6`, `castle.core 4.2.0 -> 4.4.0`, `excss 2.0.6 -> 4.1.0`, `fluentassertions 5.2.0 -> 5.10.3`, `jetbrains.annotations 2018.2.1 -> 2020.1.0`, `libgit2sharp 0.25.0 -> 0.26.2`, `libgit2sharp.nativebinaries 1.0.210 -> 2.0.306`, `microsoft-windowsapicodepack-core 1.1.3.3 -> 1.1.4`, `microsoft-windowsapicodepack-shell 1.1.3.3 -> 1.1.4`, `microsoft.applicationinsights 2.10.0-beta4 -> 2.13.1`, `microsoft.codecoverage 15.9.0 -> 16.6.1`, `microsoft.net.test.sdk 15.9.0 -> 16.6.1`, `microsoft.netcore.platforms 1.1.0 -> 1.1.0, 3.0.0, 3.1.0` (first 15 of 38)

## Verdicts (full and seeded only)
- By rule: EQ001 12876, EQ002 23, EQ003 575, EQ004 15, EQ005 192, EQ006 137
- By proofMethod, Equivalent results: congruence 12722, bounded 136, lockstep-induction 18
- Changed pairs by outcome: proved Equivalent by the solver 154 (17.7%), Unknown 556 (63.9%), Divergent 160 (18.4%; EQ002 23, EQ006 137). `unmatched-overload` Unknowns, not matched pairs: 19
- Unknown by scope: line 153, method 403. Line-scoped Unknown share: 27.5%
- Top Unknown reasons: opaque 232, abstraction 219, timeout 93, unaligned-loop 11, recursion 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 157, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): runtime-rule 476, callee 163, operation 110, type-layout 99, symbol 11, type 7, constant 3, signature 1. Of the 476 `runtime-rule` pairs, whose two texts are equal, 1 are proved, 124 are EQ006, 0 EQ002 and 351 Unknown (abstraction 176, opaque 100, timeout 69, unaligned-loop 6).
- `unbound` Unknowns: 0
- Top abstractions: delegate 241, `System.String::Concat(string,string)` 84, `System.String::IsNullOrEmpty(string)` 66, `get:System.String::get_Length()` 62, `System.String::IsNullOrWhiteSpace(string)` 50, opaque Conversion 41, `System.String::Substring(int)` 29, `op:System.String::op_Equality(string,string)` 25, `System.String::Substring(int,int)` 22, `System.String::Trim()` 21, `op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder)` 20, opaque InterpolatedString 14, `System.String::get_Chars(int)` 13, opaque LocalFunction 13, `System.String::TrimEnd(char)` 12
- Review list: 113 groups for 735 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 5.4%. Top five: `EQ003 abstraction`: 184, `EQ003 opaque:rebound-call`: 105, `EQ003 timeout`: 93, `EQ003 opaque:no-body`: 51, `EQ006 runtime-change:System.IO.Path::Combine(string,string)`: 32
- Divergent adjudication: `docs/runs/2026-10-15-precision-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown.
- EQ006 166 to 137, EQ002 29 to 23; the solver proves 154 changed pairs, from 140.
- Divergent sample (53 of 160): 1 confirmed, 17 false positive, 35 undetermined; precision 5.6% (1 of 18). False-positive causes: P2-216, P2-217 and P2-218 (new), P2-207, P2-208, P2-185, P2-186, P2-187, P2-097.
- `no-body` alone in 51 of 870 changed pairs (5.9%), the unedited `[DllImport]` bodies, no open owner: P2-214 (new). `rebound-call` alone in 121 (13.9%): P2-179.
