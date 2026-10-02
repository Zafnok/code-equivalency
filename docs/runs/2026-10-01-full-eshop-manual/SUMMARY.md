# full+execute run: eshop-manual

- Pair: human, mjrousos/UpgradeSample, legacy b4cddc2aae49 (`net472/eShopLegacyMVC.sln`), modern b4cddc2aae49 (`net6/eShopMVC.sln`)
- Corpus list: `tools/corpus/pairs.csv` (human pair, an optional extra; ticket P2-065)
- Migrated by: human (a hand-finished .NET 6 port of the net472 MVC 5 app; the hosting code is a rewrite)
- equiv: ef79ff6, wall-clock full 71s exit 5; full --execute 96s exit 5. Exit 5 is three pair-level lowering crashes (P2-083); the SARIF was written and the other 116 pairs were verified. The two runs ran one after the other.

## Phase times
From the `full` run.

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 3 | 0.000 | +0.000 |
| load-modern | 2 | 0.000 | +0.000 |
| enumerate | 2 | 0.039 | n/a |
| match | 1 | 0.001 | n/a |
| lower | 119 | 1.786 | +1.627 |
| verify | 116 | 38.730 | -16.473 |
| write | 1 | 0.122 | +0.000 |

## Load
- Projects: legacy 3 of 3 C# projects loaded, modern 2 of 2; skipped: none
- Project load rate: 100%
- Runtimes detected: legacy net461 (2 projects) and net472 (1), modern net6.0 (2)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 185 | 252 |
| analysed lines | 1637 | 2356 |

- Matched pairs 119; without opaque 93 (78.2%); whole-body opaque 0 (0.0%); congruent 90 (75.6%)
- Unchanged share: 75.6% (`pairsCongruent` / `matchedPairs`). The "unchanged files" proxy is 0%: 0 of 36 legacy `.cs` files are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 78.2%. Not the row above; ADR 0034. The two differ by the three pairs that crashed in lowering, which are neither congruent nor changed.

Top opaque reasons (legacy / modern), up to 15:

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| DelegateCreation | 10 | 10 | P2-067 |
| Conversion | 5 | 5 | none (0% of changed pairs alone) |
| InterpolatedString | 5 | 5 | none open (ADR 0039's fallback is off by default after P1-018); this run files P2-086 |
| AnonymousObjectCreation | 2 | 2 | none open (P2-024 is done and the node is still opaque); this run files P2-088 |
| DynamicMemberReference | 1 | 1 | none (3.8%, below 5%) |

## Changed code
- Changed pairs 26 of 119; without opaque 12; whole-body opaque 0
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 46.2%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 12 | n/a |
| "DelegateCreation" | 6 (23.1%) | P2-067 |
| "InterpolatedString" | 5 (19.2%) | none open; this run files P2-086 |
| "AnonymousObjectCreation" | 2 (7.7%) | none open; this run files P2-088 |
| "DynamicMemberReference" | 1 (3.8%) | none (below 5%) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 20 | 21 |
| distinct members | 12 | 12 |
| pairs with any | 9 | 9 |

- Package changes: 2 version changed, 54 legacy only, 21 modern only: entityframework 6.0.0, 6.2.0 -> 6.4.4; log4net 2.0.8 -> 2.0.14

## Verdicts (full, no --execute)
- By rule: EQ001 90, EQ002 9, EQ003 23, EQ004 133, EQ005 66, EQ006 4
- By proofMethod: congruence 90, n/a 235; solver-proved Equivalent on changed pairs (`proofMethod` other than `congruence`): 0
- Changed pairs by verdict (26): Equivalent 0, Divergent 13 (EQ002 9, EQ006 4), Unknown 13. The other 10 Unknown results are `unmatched-overload`, which are not matched pairs.
- Unknown by scope: line 5, method 18. Line-scoped Unknown share: 21.7% (5 of 23)
- Top Unknown reasons: unmatched-overload 10 (method), opaque 5 (line), abstraction 5 (method), timeout 2 (method), unaligned-loop 1 (method)
- Top abstractions (5 `abstraction` Unknown results; entries of `properties.abstractions` by kind): opaque DelegateCreation 10 (6 distinct fragments)
- Pair-level crashes: 3 tool-execution notifications, 3 unverified procedures
- Review list: n/a (P2-064 is not done)

## Tests (full only)
- The pair has no `verify_command` in the manifest and the sample ships no test project; not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a

## Replay (full --execute, M4-009; exit 5)
- Runtimes on the box: .NET Framework 4.8, Microsoft.NETCore.App 6.0.36 and 10.0.x. Microsoft.AspNetCore.App is installed only as 10.0.12, so the modern web project's own shared framework (6.x) is not present.
- Run totals: EQ001 90, EQ002 9, EQ003 23, EQ004 133, EQ005 66, EQ006 4; pair-level crashes 3 (the same three)
- Divergent results with a replay value: 13. By value: `not-constructible` 13; `reproduced` 0; `not-reproduced` 0
- `not-constructible` reasons: call trace 9, not public 3, no `System.IO.Stream` argument can be built 1
- Unknown became Divergent by observation (`proofMethod: observed`): 0
- Differential testing of Unknown pairs: 0 completed, 13 `notConstructible` (no public parameterless constructor 6, not public 6, modern side threw `System.IO.FileNotFoundException` while being set up 1)
- One Unknown changed reason between the two runs with nothing observed: `unaligned-loop` in `full`, `timeout` in `--execute` (totals timeout 2 and unaligned-loop 1, then timeout 3 and unaligned-loop 0). P2-050 owns wall-clock budgets.

## Findings
- Three pairs crash in lowering with a bare `NullReferenceException`: `eShopLegacyMVC.Controllers.CatalogController::Details(int?)`, `::Edit(int?)` and `::Delete(int?)`. The stack ends in `PureCatalogue.Binary` (`PureCatalogue.cs` line 104) from `IrLowerer.Binary` (`IrLowerer.cs` line 1366) from `IrLowerer.Branch`. Filed as P2-083.
- No changed pair is proved Equivalent: all 90 Equivalent results are congruent pairs. Of the 26 changed pairs, half are Divergent and half Unknown.
- `--execute` adds nothing on this pair: every replay and every differential test is `not-constructible`. Nine of 13 Divergents differ only in the call trace; the controllers have no parameterless constructor. No `not-reproduced` replay.
- `InterpolatedString` alone is 19.2% of changed pairs (5 of 26) and has no open owner: P2-086. `AnonymousObjectCreation` alone is 7.7% (2 of 26) and has no open owner: P2-088. `DelegateCreation` alone is 23.1%, owned by P2-067.
- 133 added and 66 removed procedures (EQ004, EQ005): the MVC 5 hosting code was replaced by ASP.NET Core's, as the manifest says.
