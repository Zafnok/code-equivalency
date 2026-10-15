# full run: gitextensions-9860

- Pair: human, gitextensions/gitextensions PR #9860 (net5.0 to net6.0, a bump with fixes), legacy bcd0c2617bdd, modern 37797ea4dd74
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: bfbf413e, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 271s (4m31s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-212: the five large pairs one after the other from one build, on a box that ran nothing else. The last summary of this pair is `docs/runs/2026-10-12-full-gitextensions-9860/SUMMARY.md`; `docs/runs/2026-10-15-precision-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 43 | 0.001 | +0.001 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.546 | n/a |
| match | 1 | 0.023 | n/a |
| lower | 14073 | 75.505 | +35.240 |
| verify | 14073 | 159.267 | -40.103 |
| write | 1 | 0.241 | +0.000 |

## Load
- Projects: legacy 43 with a detected runtime, modern 43; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy Setup; modern Setup
- Runtimes detected (`run.properties.runtimes`): legacy net5.0 (43) [source: attribute 42, host 1]; modern net6.0 (43) [source: attribute 42, host 1]

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14075 | 14075 |
| analysed lines | 189310 | 189332 |

- Matched pairs 14073; without opaque 12108 (86.0%); whole-body opaque 126 (0.9%); congruent 13540 (96.2%)
- Unchanged share: 96.2% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 1607 of 1623 legacy `.cs` files (248420 of 254577 lines, 97.6%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 96.2%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| InstanceReference | 416 | 416 | none |
| Conversion | 246 | 246 | none open (P2-123 done) |
| Binary | 228 | 228 | none open (P2-087 done) |
| LocalFunction | 212 | 212 | P2-178 (one form) |
| DelegateCreation | 208 | 212 | P2-182 (OpenRA forms; P2-136 done) |
| CaughtException | 113 | 113 | P2-180 |
| Layout | 111 | 111 | none |
| switch-pattern | 111 | 111 | none open (P2-122 done) |
| iterator | 73 | 73 | none |
| InterpolatedString | 72 | 65 | P2-102 (one form; P2-139 counted the rest) |
| ArrayElementReference | 61 | 61 | P2-183 (OpenRA forms) |
| DefaultValue | 53 | 53 | none open (P2-138 done) |
| no-body | 53 | 53 | none open (P2-165 done) |
| Tuple | 48 | 48 | none |
| ref-argument | 44 | 57 | none |

## Changed code
- Changed pairs 533 of 14073; without opaque 307; whole-body opaque 6
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 57.6%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 307 | n/a |
| "InterpolatedString" | 38 | P2-102 (one form; P2-139 counted the rest) |
| "DelegateCreation" | 28 | P2-182 (OpenRA forms; P2-136 done) |
| "LocalFunction" | 25 | P2-178 (one form) |
| "rebound-call" | 25 | P2-179 |
| "CaughtException" | 11 | P2-180 |
| "InterpolatedString+switch-pattern" | 6 | P2-102 (one form; P2-139 counted the rest); none open (P2-122 done) |
| "Layout" | 6 | none |
| "iterator" | 6 | none |
| "Binary" | 5 | none open (P2-087 done) |
| "InterpolatedString+ref-argument" | 5 | P2-102 (one form; P2-139 counted the rest); none |
| "ref-argument" | 5 | none |
| "Conversion" | 4 | none open (P2-123 done) |
| "CaughtException+InterpolatedString" | 3 | P2-180; P2-102 (one form; P2-139 counted the rest) |
| "Conversion+Layout" | 3 | none open (P2-123 done); none |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 10 | 10 |
| distinct members | 9 | 9 |
| pairs with any | 10 | 10 |

- Package changes: 20 version changed, 3 legacy only, 1 modern only; `appinsights.windowsdesktop 2.17.10 -> 2.18.1`, `dotnetruntimebootstrapper 2.0.2 -> 2.0.3`, `gitinfo 2.1.2 -> 2.2.0`, `microsoft.applicationinsights 2.17.0 -> 2.18.0`, `microsoft.applicationinsights.dependencycollector 2.17.0 -> 2.18.0`, `microsoft.codeanalysis.common 3.10.0 -> 4.0.1`, `microsoft.codeanalysis.workspaces.common 3.10.0 -> 4.0.1`, `microsoft.codecoverage 16.6.1 -> 17.0.0`, `microsoft.net.test.sdk 16.6.1 -> 17.0.0`, `microsoft.testplatform.objectmodel 16.6.1 -> 17.0.0`, `microsoft.testplatform.testhost 16.6.1 -> 17.0.0`, `microsoft.toolkit.highperformance 7.0.2 -> 7.1.2`, `microsoft.visualstudio.threading 16.10.56 -> 17.0.64`, `microsoft.visualstudio.threading.analyzers 16.10.56 -> 17.0.64` (first 15 of 20)

## Verdicts (full and seeded only)
- By rule: EQ001 13861, EQ002 6, EQ003 226, EQ004 2, EQ005 2, EQ006 0
- By proofMethod, Equivalent results: congruence 13540, bounded 299, lockstep-induction 22
- Changed pairs by outcome: proved Equivalent by the solver 321 (60.2%), Unknown 206 (38.6%), Divergent 6 (1.1%; EQ002 6, EQ006 0). `unmatched-overload` Unknowns, not matched pairs: 20
- Unknown by scope: line 145, method 61. Line-scoped Unknown share: 70.4%
- Top Unknown reasons: opaque 158, timeout 28, abstraction 19, unaligned-loop 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 58, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): context 453, callee 47, type-layout 18, runtime-rule 6, operation 5, type 2, symbol 1, constant 1. Of the 6 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 0 are EQ006, 0 EQ002 and 6 Unknown (timeout 3, opaque 2, abstraction 1).
- `unbound` Unknowns: 0
- Top abstractions: delegate 51, `System.String::Concat(string,string)` 7, `System.String::IsNullOrWhiteSpace(string)` 4, `System.String::IsNullOrEmpty(string)` 4, opaque LocalFunction 4, `op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder)` 3, `System.String::Trim()` 2, `System.String::ToLowerInvariant()` 2, `System.String::EndsWith(string,System.StringComparison)` 2, opaque Conversion 2, `get:System.String::get_Length()` 2, opaque Tuple 2, opaque ref-argument 2
- Review list: 40 groups for 232 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 1.6%. Top five: `EQ003 opaque:InterpolatedString`: 46, `EQ003 timeout`: 28, `EQ003 opaque:DelegateCreation`: 26, `EQ003 opaque:rebound-call`: 25, `EQ003 unmatched-overload`: 20
- Divergent adjudication: `docs/runs/2026-10-15-precision-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown.
- EQ006 4 to 0 (P2-163) and EQ002 10 to 6 (P2-164). All 6 adjudicated: 5 confirmed (P2-124's), 1 false positive, `PatchManager::ToPatch(...)`, owned by P2-211. Precision 83.3% (5 of 6).
- 453 of 533 changed pairs are not congruent by `context` (the interpolated-string binding that differs between net5.0 and net6.0); the solver proves 298 of them.
- `InterpolatedString` alone in 38 of 533 (7.1%): P2-102 and P2-139, as P2-124 found. `DelegateCreation` alone in 28 (5.3%): P2-182.
