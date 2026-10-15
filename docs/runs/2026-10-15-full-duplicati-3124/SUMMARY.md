# full run: duplicati-3124

- Pair: human, duplicati/duplicati PR #3124 (.NET Framework to .NET 5, first step), legacy beaf03562fdc, modern 4f565a004f52
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream pull request)
- equiv: bfbf413e, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 447s (7m27s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 `run.properties.unverified` entries, 0 tool execution notifications. Ticket P2-212: the five large pairs one after the other from one build, on a box that ran nothing else but a file-reading trace agent of the adjudication. The last summary of this pair is `docs/runs/2026-10-13-full-duplicati-3124/SUMMARY.md`; `docs/runs/2026-10-15-precision-verdict.md` sets this run beside it.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 51 | 0.001 | +0.001 |
| load-modern | 52 | 0.000 | +0.000 |
| enumerate | 2 | 0.243 | n/a |
| match | 1 | 0.009 | n/a |
| lower | 6294 | 38.953 | +11.032 |
| verify | 6294 | 380.392 | -91.532 |
| write | 1 | 0.178 | +0.000 |

## Load
- Projects: legacy 51 with a detected runtime, modern 52; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy none; modern none
- Runtimes detected (`run.properties.runtimes`): legacy net471 (51) [source: attribute 51]; modern net5.0 (52) [source: attribute 52]

## Census
| | legacy | modern |
|---|---|---|
| procedures | 6359 | 6415 |
| analysed lines | 68706 | 69205 |

- Matched pairs 6294; without opaque 5514 (87.6%); whole-body opaque 119 (1.9%); congruent 5639 (89.6%)
- Unchanged share: 89.6% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 421 of 739 legacy `.cs` files (77615 of 125501 lines, 61.8%) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 89.6%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| CaughtException | 193 | 193 | P2-180 |
| iterator | 99 | 99 | none |
| rethrow | 94 | 95 | none |
| Binary | 91 | 91 | none open (P2-087 done) |
| TranslatedQuery | 69 | 69 | none |
| Conversion | 67 | 67 | none open (P2-123 done) |
| DelegateCreation | 56 | 52 | P2-182 (OpenRA forms; P2-136 done) |
| ArrayCreation | 54 | 54 | none |
| InstanceReference | 51 | 51 | none |
| call-throw-in-try | 40 | 40 | none |
| ref-argument | 39 | 39 | none |
| Layout | 29 | 37 | none |
| rebound-call | 28 | 28 | P2-179 |
| Throw | 24 | 24 | none |
| ArrayElementReference | 22 | 22 | P2-183 (OpenRA forms) |

## Changed code
- Changed pairs 655 of 6294; without opaque 383; whole-body opaque 41
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 58.5%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 383 | n/a |
| "CaughtException" | 36 | P2-180 |
| "iterator" | 21 | none |
| "CaughtException+rethrow" | 19 | P2-180; none |
| "no-body" | 19 | none open (P2-165 done) |
| "rebound-call" | 14 | P2-179 |
| "Binary" | 10 | none open (P2-087 done) |
| "DelegateCreation" | 10 | P2-182 (OpenRA forms; P2-136 done) |
| "TranslatedQuery" | 9 | none |
| "Conversion" | 8 | none open (P2-123 done) |
| "Layout" | 8 | none |
| "ArrayCreation" | 7 | none |
| "CaughtException+Conversion" | 7 | P2-180; none open (P2-123 done) |
| "ref-argument" | 7 | none |
| "CaughtException+Conversion+rethrow" | 6 | P2-180; none open (P2-123 done); none |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 1032 | 1029 |
| distinct members | 70 | 71 |
| pairs with any | 431 | 430 |

- Package changes: 16 version changed, 9 legacy only, 145 modern only; `alphavss 1.4.0 -> 2.0.0`, `fluentftp 27.1.1 -> 28.0.5`, `mailkit 2.3.1.6 -> 2.4.1`, `microsoft.rest.clientruntime.azure 3.3.19 -> 3.3.18`, `mimekit 2.3.1 -> 2.4.1`, `minio 3.1.7 -> 3.1.8`, `newtonsoft.json 12.0.2 -> 12.0.3`, `ngettext 0.6.4 -> 0.6.5`, `sharpaescrypt.dll 1.3.3 -> 1.3.4`, `ssh.net 2020.0.1 -> 2016.1.0, 2020.0.1`, `system.reactive 4.0.0 -> 4.1.2, 4.3.2`, `system.reactive.linq 4.0.0 -> 4.3.2` (first 15 of 16)

## Verdicts (full and seeded only)
- By rule: EQ001 5838, EQ002 23, EQ003 322, EQ004 121, EQ005 65, EQ006 117
- By proofMethod, Equivalent results: congruence 5639, bounded 167, lockstep-induction 32
- Changed pairs by outcome: proved Equivalent by the solver 199 (30.4%), Unknown 316 (48.2%), Divergent 140 (21.4%; EQ002 23, EQ006 117). `unmatched-overload` Unknowns, not matched pairs: 6
- Unknown by scope: line 71, method 245. Line-scoped Unknown share: 22.5%
- Top Unknown reasons: opaque 128, timeout 115, abstraction 64, unaligned-loop 8, recursion 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 238, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): operation 332, runtime-rule 236, callee 78, symbol 5, type 2, signature 1, constant 1. Of the 236 `runtime-rule` pairs, whose two texts are equal, 0 are proved, 102 are EQ006, 0 EQ002 and 134 Unknown (timeout 53, abstraction 48, opaque 30, unaligned-loop 3).
- `unbound` Unknowns: 0
- Top abstractions: delegate 53, `System.String::Concat(string,string)` 30, `System.String::IsNullOrEmpty(string)` 20, `System.String::IsNullOrWhiteSpace(string)` 20, `get:System.String::get_Length()` 13, `System.String::StartsWith(string,System.StringComparison)` 11, `op:Duplicati.Library.Utility.TempFile::op_Implicit(Duplicati.Library.Utility.TempFile)` 10, `System.String::Substring(int,int)` 9, `System.String::get_Chars(int)` 6, `System.String::Equals(string,string,System.StringComparison)` 6, `op:System.String::op_Equality(string,string)` 6, `op:System.String::op_Inequality(string,string)` 5, `f64.div` 4, `conv.f64.i32` 4, `System.String::Trim()` 4
- Review list: 82 groups for 462 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 7.3%. Top five: `EQ003 timeout`: 115, `EQ003 abstraction`: 59, `EQ006 runtime-change:System.IO.Path::Combine(string,string)`: 26, `EQ003 opaque:CaughtException`: 21, `EQ003 opaque:CaughtException+rethrow`: 21
- Divergent adjudication: `docs/runs/2026-10-15-precision-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- No crash, no skipped project, no `unbound` Unknown.
- EQ006 142 to 117. All 11 of P2-192's EQ002 are still EQ002: the run passes no call-identity declaration, and the pair has no committed config: P2-213 (new).
- Divergent sample (53 of 140): 0 confirmed, 21 false positive, 32 undetermined; precision 0% (0 of 21). False-positive causes: P2-213, P2-216, P2-217 and P2-218 (new), P2-193, P2-205, P2-206.
- `CaughtException` alone in 36 of 655 changed pairs (5.5%): P2-180.
