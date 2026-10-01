# full+execute run: eshop-upgrade-assistant

- Pair: tool, mjrousos/UpgradeSample, legacy b4cddc2aae49 (`net472/eShopLegacyMVC.sln`), modern b4cddc2aae49 (`UpgradeAssistant-Output/eShopLegacyMVC.sln`)
- Corpus list: `tools/corpus/pairs.csv` (tool pair, an optional extra; ticket P2-065)
- Migrated by: .NET Upgrade Assistant (raw output, as committed upstream)
- equiv: ef79ff6, wall-clock full 28s exit 4; full --execute 14s exit 4. Exit 4 is a skipped project (below). The two runs ran one after the other, after the `eshop-manual` runs on the same checkout had finished.

## Phase times
From the `full` run.

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 3 | 0.000 | +0.000 |
| load-modern | 2 | 0.000 | +0.000 |
| enumerate | 2 | 0.027 | n/a |
| match | 1 | 0.001 | n/a |
| lower | 2 | 0.217 | +0.198 |
| verify | 2 | 0.691 | +0.515 |
| write | 1 | 0.154 | +0.000 |

## Load
- Projects: legacy 3 of 3 C# projects loaded, modern 1 of 2; skipped: modern `eShopLegacyMVC`: it does not compile
- Project load rate: 80% (4 of 5); modern side 50%
- Reason: the tool's raw output is not a compiling project. Restore succeeds, but the web project still names `System.Web.Mvc`, `System.Web.Optimization`, `System.Web.Routing` and `Autofac.Integration.Mvc`, which its new references no longer provide. The compiler errors behind the skip, by code: CS0246 45, CS0103 25, CS0234 13, CS1503 4, CS1061 4, CS0021 4, CS0266 2. All are binding errors; none is a syntax error. `equiv` skips a project with compile errors whole (M3-024), so the application's 308 procedures (both sides) are listed in `properties.unverified` and nothing in them is compared. Filed as P2-085.
- Runtimes detected: legacy net461 (2) and net472 (1), modern netstandard2.0 (1, the one project that loaded)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 55 | 2 |
| analysed lines | 1637 | 40 |

- Matched pairs 2; without opaque 2 (100%); whole-body opaque 0 (0.0%); congruent 0 (0.0%)
- Unchanged share: 0.0% (`pairsCongruent` / `matchedPairs`), over the two pairs of the one project that loaded. The "unchanged files" proxy is 38.6%: 12 of 36 legacy `.cs` files (783 of 2031 lines) are byte-identical on the modern side.
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 0.0%. Not the row above; ADR 0034.

Top opaque reasons (up to 15): none.

## Changed code
- Changed pairs 2 of 2; without opaque 2; whole-body opaque 0
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 100%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 2 | n/a |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 4 | 4 |
| distinct members | 3 | 3 |
| pairs with any | 2 | 2 |

- Package changes: 7 version changed, 25 legacy only, 124 modern only: autofac 4.9.1 -> 5.0.0; autofac.webapi2 4.3.1 -> 5.0.0; entityframework 6.0.0, 6.2.0 -> 6.4.4; microsoft.aspnet.webapi.core 5.2.7 -> 5.2.0; newtonsoft.json 12.0.1 -> 13.0.1; system.buffers 4.5.1 -> 4.4.0; system.threading.tasks.extensions 4.5.2 -> 4.3.0

## Verdicts (full, no --execute)
- By rule: EQ001 0, EQ002 0, EQ003 0, EQ004 0, EQ005 53, EQ006 2
- By proofMethod: n/a 55; solver-proved Equivalent on changed pairs: 0
- Changed pairs by verdict (2): Equivalent 0, Divergent 2 (EQ006: `eShopLegacy.Utilities.Serializing::SerializeBinary(object)` and `::DeserializeBinary(System.IO.Stream)`), Unknown 0
- Unknown by scope: line 0, method 0. Line-scoped Unknown share: n/a (no Unknown)
- Top Unknown reasons: none. Top abstractions: none.
- Pair-level crashes: 0. Tool-execution notifications: 1 (the skipped project). Unverified procedures: 308, all in the skipped project.
- Review list: n/a (P2-064 is not done)

## Tests (full only)
- No `verify_command` and no test project; not run. legacy n/a, modern n/a.
- Passed on legacy, failed on modern: n/a (the modern side does not build)

## Replay (full --execute, M4-009; exit 4)
- Run totals: EQ005 53, EQ006 2; pair-level crashes 0
- Divergent results with a replay value: 2. By value: `not-constructible` 2; `not-reproduced` 0
- `not-constructible` reasons: call trace 1, no `System.IO.Stream` argument can be built 1
- Unknown became Divergent by observation: 0. Differential testing of Unknown pairs: none to test.

## Findings
- The run compares 2 procedures of an application that has 185 on the legacy side. The migration tool's output does not compile, and a project that does not compile is skipped whole: P2-085. This is the case the product is for (a tool migrates, `equiv` says what to check), and today it gets no answer.
- The two pairs that are compared are both flagged EQ006, the binary-serialization helpers. Not adjudicated (P2-047's scope).
- No pair-level crash and no `not-reproduced` replay.
