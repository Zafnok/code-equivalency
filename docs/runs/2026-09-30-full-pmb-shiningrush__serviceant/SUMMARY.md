# full+seeded+execute run: pmb-shiningrush__serviceant

- Pair: agent, ShiningRush/ServiceAnt, legacy e36009c2ee86, modern agent migration (M4-007's, reused)
- Corpus list: Poly-MigrationBench @ pinned commit (see `tools/corpus/README.md`)
- Migrated by: general-purpose agent, 2026-09-27 (M4-007), following `tools/corpus/migration-prompt.md` verbatim; reused, not re-migrated
- equiv: bd8e379, wall-clock full 49s exit 0; seeded 91s exit 1; seeded-mech 82s exit 0; full --execute 42s exit 0. Exit 1 means Divergent results exist; no run had a pair-level crash (exit 5) or a load failure (exit 4).

## Phase times
From the `full` run.

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 6 | 0.000 | +0.000 |
| load-modern | 6 | 0.000 | +0.000 |
| enumerate | 2 | 0.018 | n/a |
| match | 1 | 0.001 | n/a |
| lower | 136 | 1.118 | +0.792 |
| verify | 136 | 35.711 | +0.230 |
| write | 1 | 0.065 | +0.000 |

## Load
- Projects: legacy 6 of 6, modern 6 of 6; skipped: none
- Project load rate: 100%

## Census
| | legacy | modern |
|---|---|---|
| procedures | 136 | 137 |
| analysed lines | 1532 | 1535 |

- Matched pairs 136; without opaque 93 (68.4%); whole-body opaque 0 (0.0%); congruent 134 (98.5%)
- Unchanged share: 98.5% (`pairsCongruent` / `matchedPairs`)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 98.5%. Not the row above; ADR 0034.

Top opaque reasons (legacy / modern), up to 15:

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| DelegateCreation | 28 | 28 | none open (M4-004 shares identical fragments; this run files P2-067) |
| Conversion | 6 | 6 | P1-014 (ADR 0039) |
| TypeOf | 6 | 6 | none (below 5%) |
| Binary | 4 | 4 | P1-014 (ADR 0039) |
| Await | 3 | 3 | none (below 5%) |
| CaughtException | 2 | 2 | none (1.1%, below 5%) |
| DefaultValue | 2 | 2 | none (0.7%, below 5%) |

## Changed code
- Changed pairs 2 of 136; without opaque 0; whole-body opaque 0
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 0.0%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "Await+CaughtException" | 1 | none (1.1%, below 5%); none (below 5%) |
| "Await+CaughtException+Conversion+DefaultValue" | 1 | P1-014 (ADR 0039); none (0.7%, below 5%); none (1.1%, below 5%); none (below 5%) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 2 | 2 |
| distinct members | 1 | 1 |
| pairs with any | 2 | 2 |

- Package changes: 3 version changed, 2 legacy only, 14 modern only: mstest.testadapter 1.1.18 -> 3.6.4; mstest.testframework 1.1.18 -> 3.6.4; newtonsoft.json 10.0.3 -> 10.0.3, 13.0.1

## Verdicts (full, no --execute)
- By rule: EQ001 134, EQ002 0, EQ003 2, EQ004 1, EQ005 0, EQ006 0
- By proofMethod: congruence 134, n/a 3; solver-proved Equivalent on changed pairs (`proofMethod` other than `congruence`): 0
- Unknown by scope: line 0, method 2. Line-scoped Unknown share: 0.0% (0 of 2)
- Top Unknown reasons: opaque 1, timeout 1
- Top abstractions (0 `abstraction` Unknown results; entries of `properties.abstractions` by kind, top 15; `opaque` is one kind because the SARIF records a fragment's fingerprint and not its reason, and 0 distinct fragments are behind it): none
- Pair-level crashes: 0 tool-execution notifications, 0 unverified procedures

## Tests (full only)
- Not rerun: the modern side is M4-007's, unchanged. M4-007 recorded modern 21 passed, 0 failed, 0 skipped; no legacy count (the old-style test projects print no result).
- Passed on legacy, failed on modern: none (M4-007)

## Seeds (seeded, hand-written; the `tools/corpus/seeds.md` catalogue, the same methods as M4-007 because its seeded copies were not kept)
| seed id | procedure identity | verdict | on the seeded line? |
|---|---|---|---|
| S01 | `ServiceAnt.IocInstaller.Castle.ServiceAntInstaller::Kernel_ComponentRegistered(string,Castle.MicroKernel.IHandler)` | EQ003 Unknown (unaligned-loop, method-scoped) | no (blast-radius miss) |
| S02 | `ServiceAnt.IocInstaller.Autofac.ServiceAntModule::RegisterHandlerType(Autofac.IComponentContext,System.Type)` | EQ003 Unknown (unaligned-loop, method-scoped) | no (blast-radius miss) |
| S03 | `ServiceAnt.InProcessServiceBus::ProcessRequest<T>(string,string)` | EQ003 Unknown (unaligned-loop, method-scoped) | no (blast-radius miss) |
| S04 | `ServiceAnt.Handler.SingletonHandlerFactory::.ctor(ServiceAnt.Handler.IHandler,System.Type)` | EQ002 Divergent | n/a |
| S05 | `ServiceAnt.Handler.TransportTray<TEntity>::.ctor(TEntity)` | EQ002 Divergent | n/a |
| S06 | `ServiceAnt.Handler.IocHandlerFactory::.ctor(ServiceAnt.Infrastructure.Dependency.IIocResolver,System.Type,System.Type)` | EQ002 Divergent | n/a |
| S07 | `ServiceAnt.Subscription.InMemorySubscriptionsManager::.ctor()` | EQ002 Divergent | n/a |

- Seeded recall: 7/7 = 100.0%. Blast-radius misses (Unknown that does not point at the seeded line): 3.
- Seeded run totals: EQ001 128, EQ002 4, EQ003 4, EQ004 1, EQ005 0, EQ006 0.

## Mechanical seeds (seeded-mech, ticket M4-010)
- Seeds: requested 300, applied 13, dropped (failed to compile) 0
- **Preserving family** (behaviour unchanged by construction):

| operator | applied | Equivalent (EQ001) | Unknown (EQ003) | Divergent (EQ002 + EQ006) | other or no result |
|---|---|---|---|---|---|
| InlineTemporary | 3 | 3 | 0 | 0 | 0 |
| IntroduceTemporary | 2 | 2 | 0 | 0 | 0 |
| InvertIf | 1 | 1 | 0 | 0 | 0 |
| RenameLocals | 3 | 3 | 0 | 0 | 0 |
| **all Preserving** | 9 | 9 | 0 | 0 | 0 |

- **Preserving Equivalent share** (`Equivalent / applied`): 9 of 9 = 100.0%. Reported only: ADR 0028 sets no threshold for it.
- **Changing family**:

| operator | applied | Equivalent (EQ001) | Unknown (EQ003) | Divergent (EQ002 + EQ006) | other or no result |
|---|---|---|---|---|---|
| FlipComparison | 3 | 0 | 2 | 0 | 1 |
| SwapArguments | 1 | 0 | 1 | 0 | 0 |
| **all Changing** | 4 | 0 | 3 | 0 | 1 |


- Changing family reading (reported only): 0 Divergent, 3 Unknown, 0 Equivalent, 1 no result, of 4.
- Recall = (EQ002 + line-scoped EQ003 on the seed's line) / confirmed behaviour-changing seeds: **not computed**. `seeds.json` records each seed's method start line, not the mutated line, so "on the seed's line" cannot be checked, and no run of the repos' tests confirmed which Changing seeds changed behaviour. The counts above are the reading (P2-063).
- Changing seeds reported Equivalent are not counted as misses. Each was read as a source diff against the unseeded modern side: they are operand swaps of `==`, `!=`, `&&` or an enum-flag `|`, which cannot change behaviour, so they are unconfirmed equivalent mutants (identities only, in `.corpus/`'s `seeds.json`).
- Unconfirmed list size: 0

## Replay (full --execute, M4-009; exit 0)
- Run totals: EQ001 134, EQ002 0, EQ003 2, EQ004 1, EQ005 0, EQ006 0; pair-level crashes 0
- Divergent results with a replay value: 0. By value: n/a; `not-reproduced` 0
- Unknown became Divergent by observation (`proofMethod: observed`): 0
- Differential testing of Unknown pairs: 0 completed, 2 `notConstructible`

## Findings
- No crash (M4-007 had one, P2-031). The congruent count rose from 113 to 134 of 136 (83.1% to 98.5%) and changed pairs fell from 23 to 2; both changed pairs still carry an `Await` opaque.
- All nine Preserving seeds are Equivalent (M4-007's Preserving false Divergent, P2-036, did not recur).
- The one Changing `SwapArguments` and two of three `FlipComparison` seeds are Unknown; one `FlipComparison` seed has no result at its identity (a generic-delegate overload the identity match could not pair).
- No new ticket from this pair.
