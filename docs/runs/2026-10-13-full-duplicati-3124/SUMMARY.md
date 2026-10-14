# full run: duplicati-3124

- Pair: human, duplicati/duplicati PR #3124 (.NET Framework to .NET 5, first step), legacy beaf03562fdc, modern 4f565a004f52
- Corpus list: `tools/corpus/pairs.csv` (human pair)
- Migrated by: human (upstream PR #3124)
- equiv: e98fd1b9, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 547s (9m07s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: no `run.properties.unverified`, 0 tool execution notifications. The box ran nothing else; it ran straight after the `gitextensions-8522` run, on the same build. The last summary of this pair is `docs/runs/2026-10-01-full-duplicati-3124/SUMMARY.md` (equiv ef79ff6), whose `full` run crashed before the first verdict (P2-082) and whose numbers come from a `--lower-only` census. This is the pair's first run to a result.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 51 | 0.002 | +0.002 |
| load-modern | 52 | 0.000 | +0.000 |
| enumerate | 2 | 0.262 | n/a |
| match | 1 | 0.012 | n/a |
| lower | 6294 | 38.592 | +9.761 |
| verify | 6294 | 477.952 | -111.132 |
| write | 1 | 0.151 | +0.000 |

## Load
- Projects: legacy 51 with a detected runtime, modern 52; skipped: none (0 legacy, 0 modern). `Duplicati.Tools`, the placeholder project P2-084 found, is no longer counted as a load failure.
- Project load rate: 100%
- Not built: none
- Runtimes detected (`run.properties.runtimes`): legacy net471 (51); modern net5.0 (52)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 6359 | 6415 |
| analysed lines | 68706 | 69205 |

- Matched pairs 6294; without opaque 5514 (87.6%); whole-body opaque 119 (1.9%); congruent 5587 (88.8%)
- Unchanged share: 88.8% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy: 421 of 739 legacy `.cs` files (77615 of 125501 lines) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 88.8%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). "none open (P2-nnn done)": the ticket that lowered part of the reason is done, and no open ticket owns what is left.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| CaughtException | 193 | 193 | P2-180 (new; P2-140 done) |
| iterator | 99 | 99 | none |
| rethrow | 94 | 95 | none |
| Binary | 91 | 91 | none open (P2-087 done) |
| TranslatedQuery | 69 | 69 | none |
| Conversion | 67 | 67 | none open (P2-123 done) |
| DelegateCreation | 56 | 52 | none open (P2-136 done) |
| ArrayCreation | 54 | 54 | none |
| InstanceReference | 51 | 51 | none |
| call-throw-in-try | 40 | 40 | none |
| ref-argument | 39 | 39 | none |
| Layout | 29 | 37 | none |
| rebound-call | 28 | 28 | P2-179 (new; P2-137 done) |
| Throw | 24 | 24 | none |
| ArrayElementReference | 22 | 22 | none |

## Changed code
- Changed pairs 707 of 6294; without opaque 425; whole-body opaque 41
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 60.1%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 425 | n/a |
| "CaughtException" | 37 | P2-180 |
| "iterator" | 21 | none |
| "CaughtException+rethrow" | 19 | P2-180; none |
| "no-body" | 19 | P2-165 |
| "rebound-call" | 14 | P2-179 |
| "DelegateCreation" | 11 | none open (P2-136 done) |
| "TranslatedQuery" | 11 | none |
| "Binary" | 10 | none open (P2-087 done) |
| "Conversion" | 9 | none open (P2-123 done) |
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

- Package changes: 16 version changed, 9 legacy only, 145 modern only. Version-changed (first 15): alphavss 1.4.0 -> 2.0.0; awssdk.core 3.3.103.37 -> 3.3.104.11; awssdk.identitymanagement 3.3.103.26 -> 3.3.104.18; awssdk.s3 3.3.104.25 -> 3.3.110.7; fluentftp 27.1.1 -> 28.0.5; mailkit 2.3.1.6 -> 2.4.1; microsoft.rest.clientruntime.azure 3.3.19 -> 3.3.18; mimekit 2.3.1 -> 2.4.1; minio 3.1.7 -> 3.1.8; newtonsoft.json 12.0.2 -> 12.0.3; ngettext 0.6.4 -> 0.6.5; sharpaescrypt.dll 1.3.3 -> 1.3.4; ssh.net 2020.0.1 -> 2016.1.0, 2020.0.1; system.reactive 4.0.0 -> 4.1.2, 4.3.2; system.reactive.linq 4.0.0 -> 4.3.2

## Verdicts (full and seeded only)
- By rule: EQ001 5776, EQ002 24, EQ003 358, EQ004 121, EQ005 65, EQ006 142
- By proofMethod, Equivalent results: congruence 5587, bounded 162, lockstep-induction 27
- Changed pairs by outcome: proved Equivalent by the solver 189 (26.7%), Unknown 352 (49.8%; 358 less 6 `unmatched-overload`), Divergent 166 (23.5%; EQ002 24, EQ006 142)
- Unknown by scope: line 72, method 286. Line-scoped Unknown share: 20.1%
- Top Unknown reasons: opaque 125, timeout 124, abstraction 92, unaligned-loop 8, unmatched-overload 6, recursion 3
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 255, wallClock 0
- Why a changed pair is not congruent (`properties.notCongruent`, first component): operation 332, runtime-rule 269, callee 78, no-fingerprint 19, symbol 5, type 2, signature 1, constant 1. Of the 269 `runtime-rule` pairs, whose two texts are equal, 1 is proved, 122 are EQ006 and 146 Unknown (abstraction 66, timeout 62, opaque 13, unaligned-loop 4, recursion 1).
- `unbound` Unknowns: 0
- Top abstractions: delegate 27, `System.String::IsNullOrEmpty(string)` 17, `System.String::Concat(string,string)` 17, `System.String::IsNullOrWhiteSpace(string)` 13, `get:System.String::get_Length()` 9, `System.String::StartsWith(string,System.StringComparison)` 8, `System.String::Trim()` 8, `System.String::Substring(int,int)` 6, `System.String::Equals(string,string,System.StringComparison)` 5, `op:Duplicati.Library.Utility.TempFile::op_Implicit(Duplicati.Library.Utility.TempFile)` 5, `System.String::get_Chars(int)` 4, `op:System.String::op_Equality(string,string)` 4, `System.String::EndsWith(string,System.StringComparison)` 3, `op:System.String::op_Inequality(string,string)` 3, `System.Char::ToString()` 3
- Review list: 87 groups for 524 flagged results (EQ002 + EQ003 + EQ006), from `run.properties.reviewList`; flagged results as a share of matched pairs: 8.3%. Top five: `EQ003 timeout`: 124, `EQ003 abstraction`: 86, `EQ006 runtime-change:System.IO.Path::Combine(string,string)`: 24, `EQ003 opaque:iterator`: 21, `EQ003 opaque:CaughtException`: 20
- Divergent adjudication: `docs/runs/2026-10-14-migrations-verdict.md`.

## Tests (full only)
- No `verify_command` in the manifest. Not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Findings
- First run to a result: no crash (P2-082 and P2-083 are done), no skipped project, no `unbound` Unknown.
- Unedited bodies a runtime rule keeps out of congruence: 269 of 707 changed pairs, 1 proved: P2-181 (new).
- `CaughtException` alone in 37 of 707 changed pairs (5.2%): P2-180 (new).
- Divergent sample (54 of 166): 0 confirmed, 27 false positive, 27 undetermined; precision 0% (0 of 27). False-positive causes: P2-184, P2-188, P2-189, P2-190, P2-192, P2-193 (new).
- A bug the migration introduced, reported EQ002 and found by the hand trace, not executed: `Duplicati.Library.Backend.S3IAM::CreateUnprivilegedUser(string,string,string)` drops the task of an asynchronous call.
