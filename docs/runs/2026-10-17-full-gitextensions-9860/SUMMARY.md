# full run: gitextensions-9860

- Pair: human, gitextensions/gitextensions PR #9860 (net5.0 to net6.0, a bump with fixes), legacy bcd0c2617bdd, modern 37797ea4dd74
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: b0f56ff8, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 270s (4m30s), exit 1. Exit 1 means Divergent results exist (here a witnessed runtime exposure counts as one, ADR 0060 decision 5). No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-238: the five large pairs one after the other from one Release build, on the checkouts P2-212 fetched and restored, nothing else started from this session while they ran. The last summary of this pair is `docs/runs/2026-10-15-full-gitextensions-9860/SUMMARY.md`; `docs/runs/2026-10-17-runtime-exposure-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 43 | 0.001 | +0.000 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.563 | n/a |
| match | 1 | 0.035 | n/a |
| lower | 14073 | 80.001 | +29.900 |
| verify | 14073 | 156.256 | -45.905 |
| write | 1 | 0.270 | +0.000 |

## Load
- Projects: legacy 43 with a detected runtime, modern 43; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Runtimes detected (`run.properties.runtimes`): legacy net5.0 (43) [source: attribute 42, host 1]; modern net6.0 (43) [source: attribute 42, host 1]

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14075 | 14075 |
| analysed lines | 189310 | 189332 |

- Matched pairs 14073; without opaque 12132 (86.2%); whole-body opaque 126 (0.9%); congruent 13540 (96.2%)
- Unchanged share: 96.2% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 1607 of 1623 legacy `.cs` files (248420 of 254577 lines, 97.6%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 96.2%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| InstanceReference | 416 | 416 | none |
| Conversion | 246 | 246 | none open (P2-123 done) |
| Binary | 228 | 228 | none open (P2-087 done) |
| LocalFunction | 212 | 212 | P2-178 (one form) |
| DelegateCreation | 180 | 184 | none open (P2-182 done) |
| Layout | 111 | 111 | none |
| switch-pattern | 111 | 111 | none open (P2-122 done) |
| CaughtException | 107 | 107 | none open (P2-180 done) |
| iterator | 73 | 73 | none |
| InterpolatedString | 72 | 65 | P2-102 (one form; P2-139 counted the rest) |
| ArrayElementReference | 61 | 61 | P2-183 (OpenRA forms) |
| DefaultValue | 53 | 53 | none open (P2-138 done) |
| no-body | 53 | 53 | none open (P2-165 done) |
| ref-argument | 44 | 56 | none |
| Tuple | 48 | 48 | none |

## Changed code
- Changed pairs 533 of 14073; without opaque 311; whole-body opaque 6
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 58.3%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 311 | n/a |
| "InterpolatedString" | 39 | P2-102 (one form; P2-139 counted the rest) |
| "DelegateCreation" | 27 | none open (P2-182 done) |
| "LocalFunction" | 25 | P2-178 (one form) |
| "rebound-call" | 25 | none open (P2-179 done) |
| "CaughtException" | 10 | none open (P2-180 done) |
| "InterpolatedString+switch-pattern" | 6 | P2-102 (one form; P2-139 counted the rest); none open (P2-122 done) |
| "Layout" | 6 | none |
| "iterator" | 6 | none |
| "Binary" | 5 | none open (P2-087 done) |
| "InterpolatedString+ref-argument" | 5 | P2-102 (one form; P2-139 counted the rest); none |
| "Conversion" | 4 | none open (P2-123 done) |
| "ref-argument" | 4 | none |
| "Conversion+Layout" | 3 | none open (P2-123 done); none |
| "LocalFunction+Tuple" | 3 | P2-178 (one form); none |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 10 | 10 |
| distinct members | 9 | 9 |
| pairs with any | 10 | 10 |

- Package changes: 20 version changed, 3 legacy only, 1 modern only; `appinsights.windowsdesktop 2.17.10 -> 2.18.1`, `dotnetruntimebootstrapper 2.0.2 -> 2.0.3`, `gitinfo 2.1.2 -> 2.2.0`, `microsoft.applicationinsights 2.17.0 -> 2.18.0`, `microsoft.applicationinsights.dependencycollector 2.17.0 -> 2.18.0`, `microsoft.codeanalysis.common 3.10.0 -> 4.0.1`, `microsoft.codeanalysis.workspaces.common 3.10.0 -> 4.0.1`, `microsoft.codecoverage 16.6.1 -> 17.0.0`, `microsoft.net.test.sdk 16.6.1 -> 17.0.0`, `microsoft.testplatform.objectmodel 16.6.1 -> 17.0.0`, `microsoft.testplatform.testhost 16.6.1 -> 17.0.0`, `microsoft.toolkit.highperformance 7.0.2 -> 7.1.2`, `microsoft.visualstudio.threading 16.10.56 -> 17.0.64`, `microsoft.visualstudio.threading.analyzers 16.10.56 -> 17.0.64` (first 15 of 20) (the same checkouts as the last summary)

## Verdicts (full and seeded only)
- By rule: EQ001 13864, EQ002 5, EQ003 218, EQ004 2, EQ005 2, EQ006 0, EQ007 6 (witnessed 0, not 6)
- Runtime exposures (EQ007, ADR 0060): an unedited body a runtime change reaches. Witnessed ones carry `properties.model` and count as Divergent for the exit code; the others carry `unknownReason` and count as Unknown. Divergent is EQ002 + EQ006 alone, and Unknown EQ003 alone, so the three never overlap.
- By proofMethod, Equivalent results: congruence 13540, bounded 299, lockstep-induction 25
- Changed pairs by outcome: proved Equivalent by the solver 324 (60.8%), Unknown 198 (37.1%), Divergent 5 (0.9%; EQ002 5, EQ006 0), runtime exposure 6 (1.1%; witnessed 0, not 6). `unmatched-overload` Unknowns, not matched pairs: 20
- Unknown by scope (EQ003): line 143, method 55. Line-scoped Unknown share: 72.2%
- Top Unknown reasons (EQ003): opaque 156, timeout 23, abstraction 18, unaligned-loop 1
- Runtime exposures without a witness, by `unknownReason`: timeout 3, opaque 2, abstraction 1; by first rule (up to 8): `System.Int32::ToString(string` 2, `System.Windows.Forms.TableLayoutPanel::set_CellBorderStyle(` 1, `System.Windows.Forms.TableLayoutPanel::set_GrowStyle(` 1, `System.Environment::get_ProcessorCount(` 1, `System.String::Remove(int)` 1
- Runtime exposures with a witness, by first rule (up to 8): none
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 56, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): context 453, callee 47, type-layout 18, runtime-rule 6, operation 5, type 2, symbol 1, constant 1. Of the 6 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 0 EQ002 and 6 EQ007; none is EQ006 or EQ003, as ADR 0060 decision 2 has it.
- `unbound` Unknowns: 0
- Top abstractions, over every `abstraction` result (EQ003 and unwitnessed EQ007): delegate 51, `System.String::Concat(string,string)` 6, `System.String::IsNullOrWhiteSpace(string)` 4, `System.String::IsNullOrEmpty(string)` 4, opaque LocalFunction 4, `op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder)` 3, `System.String::Trim()` 2, `System.String::ToLowerInvariant()` 2, `System.String::EndsWith(string,System.StringComparison)` 2, opaque Conversion 2, `get:System.String::get_Length()` 2, opaque Tuple 2, opaque ref-argument 2
- Review list: 42 groups for 229 flagged results (EQ002 + EQ003 + EQ006 + EQ007), from `run.properties.reviewList`; flagged results as a share of matched pairs: 1.6%. Top five: `EQ003 opaque:InterpolatedString`: 47, `EQ003 opaque:DelegateCreation`: 27, `EQ003 opaque:rebound-call`: 26, `EQ003 timeout`: 23, `EQ003 unmatched-overload`: 20
- Divergent adjudication: `docs/runs/2026-10-17-runtime-exposure-verdict.md` (P2-212's sample, with P2-237's execution, split by these runs' rule ids).

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown, no `run.properties.unverified` entry.
- Against P2-212 (`bfbf413e`): the 6 `runtime-rule` Unknowns are the 6 EQ007, none witnessed, as ADR 0060 projected. EQ002 6 to 5: `PatchManager::ToPatch(...)`, P2-212's one false positive here, is Equivalent at this commit (P2-211). The solver proves 324 changed pairs, from 321; Unknown 206 to 198.
- Divergent precision: 100% (5 of 5), P2-212's five confirmed EQ002, all still EQ002.
