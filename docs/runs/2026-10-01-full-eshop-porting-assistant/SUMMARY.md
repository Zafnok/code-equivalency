# full+execute run: eshop-porting-assistant

- Pair: tool, mjrousos/UpgradeSample, legacy b4cddc2aae49 (`net472/eShopLegacyMVC.sln`), modern b4cddc2aae49 (`PortingAssistant-Output/eShopLegacyMVC.sln`)
- Corpus list: `tools/corpus/pairs.csv` (tool pair, an optional extra; ticket P2-065)
- Migrated by: AWS Porting Assistant for .NET (raw output, as committed upstream)
- equiv: ef79ff6, wall-clock full 17s exit 4; full --execute 13s exit 4. Exit 4 is a skipped project (below). The two runs ran one after the other, after the other two eShop pairs on the same checkout had finished.

## Phase times
From the `full` run.

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 3 | 0.000 | +0.000 |
| load-modern | 2 | 0.001 | +0.000 |
| enumerate | 2 | 0.040 | n/a |
| match | 1 | 0.002 | n/a |
| lower | 2 | 0.273 | +0.260 |
| verify | 2 | 0.236 | +0.156 |
| write | 1 | 0.170 | +0.000 |

## Load
- Projects: legacy 3 of 3 C# projects loaded, modern 1 of 2; skipped: modern `eShopLegacyMVC`: it does not compile
- Project load rate: 80% (4 of 5); modern side 50%
- Reason: the tool's raw output is not a compiling project, in two ways. Restore of the modern solution fails: NU1605, a package downgrade of `System.Diagnostics.DiagnosticSource` from 5.0.1 to 5.0.0, which the SDK treats as an error. And the web project has compile errors, by code: CS0103 19, CS1002 7, CS0246 7, CS1061 6, CS0234 6, CS1513 4, CS0617 2, CS0118 2. CS1002 and CS1513 are syntax errors, so the source does not parse as the tool left it. `equiv` skips a project with compile errors whole (M3-024), so the application's 299 procedures (both sides) are listed in `properties.unverified` and nothing in them is compared. Filed as P2-085.
- Runtimes detected: legacy net461 (2) and net472 (1), modern net6.0 (1, the one project that loaded)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 55 | 2 |
| analysed lines | 1637 | 34 |

- Matched pairs 2; without opaque 2 (100%); whole-body opaque 0 (0.0%); congruent 0 (0.0%)
- Unchanged share: 0.0% (`pairsCongruent` / `matchedPairs`), over the two pairs of the one project that loaded. The "unchanged files" proxy is 9.6%: 4 of 36 legacy `.cs` files (195 of 2031 lines) are byte-identical on the modern side.
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

- Package changes: 25 version changed, 28 legacy only, 120 modern only. Version-changed (first 15): autofac 4.9.1 -> 4.9.2; bootstrap 4.3.1 -> 4.4.0; entityframework 6.0.0, 6.2.0 -> 6.3.0; jquery 3.3.1 -> 3.4.0; jquery.validation 1.17.0 -> 1.19.1; log4net 2.0.8 -> 2.0.9; microsoft.applicationinsights 2.9.1 -> 2.10.0; microsoft.applicationinsights.dependencycollector 2.9.0 -> 2.9.1; microsoft.applicationinsights.perfcountercollector 2.9.0 -> 2.9.1; microsoft.applicationinsights.web 2.9.0 -> 2.9.1; microsoft.applicationinsights.windowsserver 2.9.0 -> 2.9.1; microsoft.applicationinsights.windowsserver.telemetrychannel 2.9.1 -> 2.10.0; microsoft.bcl.asyncinterfaces 1.1.0 -> 1.1.1; microsoft.jquery.unobtrusive.validation 3.2.11 -> 3.2.12; microsoft.net.compilers 2.10.0 -> 3.0.0. The modern side's restore failed, so its package list is what its restore wrote before the error.

## Verdicts (full, no --execute)
- By rule: EQ001 0, EQ002 0, EQ003 0, EQ004 0, EQ005 53, EQ006 2
- By proofMethod: n/a 55; solver-proved Equivalent on changed pairs: 0
- Changed pairs by verdict (2): Equivalent 0, Divergent 2 (EQ006: `eShopLegacy.Utilities.Serializing::SerializeBinary(object)` and `::DeserializeBinary(System.IO.Stream)`), Unknown 0
- Unknown by scope: line 0, method 0. Line-scoped Unknown share: n/a (no Unknown)
- Top Unknown reasons: none. Top abstractions: none.
- Pair-level crashes: 0. Tool-execution notifications: 1 (the skipped project). Unverified procedures: 299, all in the skipped project.
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
- Same outcome as `eshop-upgrade-assistant`: 2 procedures compared, the application skipped because the tool's output does not compile (here it does not even parse). P2-085.
- The restore was run as the skill says and failed on NU1605. It was not rerun with the warning suppressed: the syntax errors would skip the project either way.
- No pair-level crash and no `not-reproduced` replay.
