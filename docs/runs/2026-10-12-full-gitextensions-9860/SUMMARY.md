# full run: gitextensions-9860

- Pair: human, gitextensions/gitextensions PR #9860 (net5.0 to net6.0, a bump with fixes), legacy bcd0c2617bdd, modern 37797ea4dd74
- Corpus list: `tools/corpus/pairs.csv`
- Migrated by: human (upstream PR #9860)
- equiv: 1d767f54, mode full, compare mode quick (the default, ADR 0052: bound 3, resourceLimit 2000000, timeoutMs 60000), `--jobs 4`, wall-clock 264s (4m24s), exit 1. Exit 1 means Divergent results exist. No pair-level crash: 0 unverified procedures, 0 tool execution notifications. The box ran nothing else. It ran straight after the `jellyfin-13023` run, on the same build.
- A second run in compare mode thorough followed on the same build and checkouts (`--jobs 4`, 4899s, exit 1, no crash). Every number below is the quick run's unless it says thorough; the thorough run's differences are under "Thorough run".
- Ticket P2-124. Earlier summaries of this pair: `docs/runs/2026-10-03-full-gitextensions-9860/SUMMARY.md` (P2-066) and `docs/runs/2026-10-08-full-gitextensions-9860/SUMMARY.md`. `docs/runs/2026-10-12-upgrade-verdict.md` sets this run beside P2-066's.

## Phase times
| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 43 | 0.001 | +0.001 |
| load-modern | 43 | 0.000 | +0.000 |
| enumerate | 2 | 0.494 | n/a |
| match | 1 | 0.037 | n/a |
| lower | 14073 | 69.845 | +31.811 |
| verify | 14073 | 160.719 | -9.031 |
| write | 1 | 0.255 | +0.000 |

## Load
- Projects: legacy 43 with a detected runtime, modern 43; skipped: none (0 legacy, 0 modern)
- Project load rate: 100%
- Not built: legacy Setup; modern Setup
- Runtimes detected (`run.properties.runtimes`): legacy net5.0 (43); modern net6.0 (43). 42 on each side come from the target framework attribute and 1, `Git.hub`, from the host project.
- Restore: `dotnet restore --force` on both sides, as the skill writes it.

## Census
| | legacy | modern |
|---|---|---|
| procedures | 14075 | 14075 |
| analysed lines | 189310 | 189332 |

- Matched pairs 14073; without opaque 12067 (85.7%); whole-body opaque 126 (0.9%); congruent 13468 (95.7%)
- Unchanged share: 95.7% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy is 99.0%: 1607 of 1623 legacy `.cs` files are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 95.7%. Not the row above; ADR 0034.

Top opaque reasons (up to 15). Owning tickets were not looked up again for this rerun, except where the verdict names one.

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| InstanceReference | 419 | 419 | n/a |
| Conversion | 246 | 246 | n/a |
| Binary | 227 | 227 | n/a |
| LocalFunction | 211 | 211 | n/a |
| DelegateCreation | 208 | 212 | n/a |
| CaughtException | 113 | 113 | n/a |
| Layout | 111 | 111 | n/a |
| switch-pattern | 111 | 111 | n/a |
| DeconstructionAssignment | 77 | 77 | n/a |
| iterator | 73 | 73 | n/a |
| InterpolatedString | 71 | 64 | P2-102 (one form; P2-139 counted the rest) |
| ArrayElementReference | 61 | 61 | n/a |
| DefaultValue | 54 | 54 | n/a |
| no-body | 53 | 53 | P2-165 |
| Tuple | 48 | 48 | n/a |

## Changed code
- Changed pairs 605 of 14073; without opaque 314; whole-body opaque 59
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 51.9%
- 25 changed pairs are in the 16 files the pull request edits; 580 are in byte-identical files (P2-066: 25 and 697).

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 314 | n/a |
| "no-body" | 53 | P2-165 |
| "InterpolatedString" | 38 | P2-102 (one form; P2-139 counted the rest) |
| "DelegateCreation" | 28 | n/a |
| "LocalFunction" | 26 | n/a |
| "rebound-call" | 25 | n/a |
| "CaughtException" | 11 | n/a |
| "ref-argument" | 10 | n/a |
| "InterpolatedString+switch-pattern" | 6 | n/a |
| "Layout" | 6 | n/a |
| "iterator" | 6 | n/a |
| "Binary" | 5 | n/a |
| "Conversion" | 5 | n/a |
| "DeconstructionAssignment" | 5 | n/a |
| "InterpolatedString+ref-argument" | 5 | n/a |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 48 | 48 |
| distinct members | 11 | 11 |
| pairs with any | 25 | 25 |

- Package changes: n/a. `corpus.ps1 -Packages` stops with "The property 'Files' cannot be found on this object" on both pairs of this rerun.

## Verdicts (full and seeded only)
- By rule: EQ001 13768, EQ002 10, EQ003 311, EQ004 2, EQ005 2, EQ006 4
- By proofMethod, Equivalent results: congruence 13468, bounded 283, lockstep-induction 17
- Changed pairs by outcome: proved Equivalent 300 (49.6%), Unknown 291 (48.1%; 311 less 20 `unmatched-overload`), Divergent 14 (2.3%; EQ002 10, EQ006 4)
- Unknown by scope: line 157, method 154. Line-scoped Unknown share: 50.5%
- Top Unknown reasons: opaque 227, timeout 37, abstraction 25, unmatched-overload 20, recursion 1, unaligned-loop 1
- Queries the budget ended (`run.properties.queryEndings`): resourceLimit 71, wallClock 0
- `unbound` Unknowns: 0
- Top abstractions: delegate 60, `System.String::Concat(string,string)` 8, `System.String::IsNullOrEmpty(string)` 4, `op:GitExtUtils.ArgumentString::op_Implicit(GitExtUtils.ArgumentBuilder)` 4, `System.String::IsNullOrWhiteSpace(string)` 2, `System.String::Trim()` 2, `System.String::ToLowerInvariant()` 2, `System.String::EndsWith(string,System.StringComparison)` 2, opaque Conversion 2, `System.String::Split(char,System.StringSplitOptions)` 2, `get:System.String::get_Length()` 2, opaque Tuple 2, opaque ref-argument 2, `op:GitExtUtils.ArgumentString::op_Implicit(string)` 1
- Review list: 47 groups for 325 flagged results (EQ002 + EQ003 + EQ006); flagged results as a share of matched pairs: 2.3%. Top five: `EQ003 opaque:no-body`: 53, `EQ003 opaque:InterpolatedString`: 45, `EQ003 timeout`: 37, `EQ003 opaque:DelegateCreation`: 26, `EQ003 opaque:LocalFunction`: 25 (and `EQ003 opaque:rebound-call`: 25)
- EQ006 by row: `System.Windows.Forms.TreeNodeCollection::get_Item(` (changedIn net6.0) 4. No other row fires.
- Adjudication of the 14 Divergent results (5 confirmed, 9 false positives) is in the verdict file.

### Thorough run
Same build, same checkouts, `-Mode thorough` (first pass as quick; budget and IL passes at bound 3, resourceLimit 30000000, timeoutMs 600000), `--jobs 4`, 4899s (1h21m39s), exit 1, 0 notifications, 0 unverified procedures.

| phase | items | seconds |
|---|---|---|
| lower | 14073 | 80.780 |
| verify | 14073 | 160.078 |
| budget | 52 | 4147.499 |
| il | 42 | 425.799 |
| contracts | 476 | 53.369 |

- By rule: EQ001 13770, EQ002 24, EQ003 292, EQ004 2, EQ005 2, EQ006 7. Changed pairs: proved Equivalent 302 (49.9%), Unknown 272 (45.0%; 292 less 20 `unmatched-overload`), Divergent 31 (5.1%).
- Unknown by reason: opaque 226, abstraction 27, timeout 16, unaligned-loop 2, recursion 1, unmatched-overload 20. Queries the budget ended: resourceLimit 74, wallClock 0.
- The budget pass decided 20 results and the IL pass 18 (`decidedBy`). Against the quick run: 2 more pairs are Equivalent (`bounded`, both from the IL pass: `ResourceManager.LinkFactory::CreateCommitLink(GitUIPluginInterfaces.ObjectId,string,bool)` and `BugReporter.BugReportForm::ShowDialog(...)`), 19 `timeout` have another Unknown reason (`opaque` 16, `abstraction` 2, `unaligned-loop` 1), and 17 Unknowns are Divergent (16 `opaque`, 1 `timeout`).
- The 17 new Divergent: 14 EQ002 from the IL pass, all in byte-identical files and all an interpolated string that binds to `string.Format` on net5.0 and to `DefaultInterpolatedStringHandler` on net6.0 (false positives, P2-135); 3 EQ006, under the rows `TreeNodeCollection::get_Item(` (false, P2-163), `StringBuilder::AppendLine(ref ` (false, P2-164's Notes) and `Environment::get_ProcessorCount(` (undetermined). All three rows have `changedIn` net6.0. The verdict file adjudicates each.
- No Divergent of the quick run changes.

## Tests (full only)
- Not run: the pair has no `verify_command`. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a
- `--execute` was not run: Microsoft.NETCore.App 5 and Microsoft.WindowsDesktop.App 5 and 6 are not installed (ticket Out of scope).

## Findings
- EQ006 falls from 59 (P2-066) to 4: every runtime-change row now has a change point (P2-113). Of the 59, 53 are congruent, 4 proved, 1 Unknown and 1 still EQ006.
- The 4 EQ006 left all cite the `TreeNodeCollection::get_Item(` row and all read the indexer, which the change does not reach: P2-163.
- 5 EQ002 in unedited files are `StringBuilder.Append` of an interpolated string, which binds to `AppendInterpolatedStringHandler` on net6.0: P2-164.
- 53 `no-body` Unknowns (8.8% of changed pairs, the largest opaque reason) are `extern` methods whose declaration is unedited, Unknown on any pair that crosses a runtime (ADR 0054 decision 4): P2-165.
- `InterpolatedString` is the first reason of 45 Unknowns (7.4% of changed pairs). P2-139 counted its forms on this pair on 2026-10-11 and found none at 5% alone; P2-102 owns one. No new ticket.
- 2 EQ002 are new and confirmed: `GitExtensions.Plugins.Gource.GourcePlugin::DownloadFile(string,string)` and `::SearchForGourceUrl()`, where the pull request adds a `using` to a `WebClient`. Both were Unknown (`opaque`) in P2-066's run.
- Thorough only: 14 false EQ002 from the IL pass (P2-135, existing) and 3 more EQ006, 2 false and 1 undetermined.
- `corpus.ps1 -Packages` fails on this pair (see Package changes). Tooling only; no result depends on it.
