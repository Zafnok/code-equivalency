# full+seeded+execute run: pmb-lethek__signalr.extras.autofac

- Pair: agent, lethek/SignalR.Extras.Autofac, legacy 3a4ac841ad23, modern agent migration (M4-007's, reused)
- Corpus list: Poly-MigrationBench @ pinned commit (see `tools/corpus/README.md`)
- Migrated by: general-purpose agent, 2026-09-27 (M4-007), following `tools/corpus/migration-prompt.md` verbatim; reused, not re-migrated
- equiv: bd8e379, wall-clock full 8s exit 0; seeded 8s exit 1; seeded-mech 4s exit 0; full --execute 4s exit 0. Exit 1 means Divergent results exist; no run had a pair-level crash (exit 5) or a load failure (exit 4).

## Phase times
From the `full` run.

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 2 | 0.000 | +0.000 |
| load-modern | 2 | 0.000 | +0.000 |
| enumerate | 2 | 0.015 | n/a |
| match | 1 | 0.001 | n/a |
| lower | 25 | 0.549 | +0.304 |
| verify | 25 | 0.000 | +0.000 |
| write | 1 | 0.083 | +0.000 |

## Load
- Projects: legacy 2 of 2, modern 2 of 2; skipped: none
- Project load rate: 100%
- Not built (outside the default configuration): legacy 3 (ExampleUsingOWIN, ExampleUsingIIS, `_build`), modern 3 (same)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 25 | 26 |
| analysed lines | 389 | 394 |

- Matched pairs 25; without opaque 20 (80.0%); whole-body opaque 0 (0.0%); congruent 25 (100.0%)
- Unchanged share: 100.0% (`pairsCongruent` / `matchedPairs`)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 100.0%. Not the row above; ADR 0034.

Top opaque reasons (legacy / modern), up to 15:

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| DelegateCreation | 5 | 5 | none open (M4-004 shares identical fragments; this run files P2-067) |
| Conversion | 1 | 1 | P1-014 (ADR 0039) |

## Changed code
- Changed pairs 0 of 25; without opaque 0; whole-body opaque 0
- Lowerable share (changedPairsWithoutOpaque / changedPairs): n/a: no changed pairs

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: 3 version changed, 0 legacy only, 11 modern only: autofac 6.5.0 -> 6.5.0, 7.0.1; microsoft.owin 2.1.0 -> 2.1.0, 4.2.2; newtonsoft.json 6.0.4 -> 13.0.1, 13.0.3, 6.0.4 (the modern side also resolves a newer version for a project the legacy side does not build)

## Verdicts (full, no --execute)
- By rule: EQ001 25, EQ002 0, EQ003 0, EQ004 1, EQ005 0, EQ006 0
- By proofMethod: congruence 25, n/a 1; solver-proved Equivalent on changed pairs (`proofMethod` other than `congruence`): 0
- Unknown by scope: line 0, method 0. Line-scoped Unknown share: n/a (0 of 0)
- Top Unknown reasons: none
- Top abstractions (0 `abstraction` Unknown results; entries of `properties.abstractions` by kind, top 15; `opaque` is one kind because the SARIF records a fragment's fingerprint and not its reason, and 0 distinct fragments are behind it): none
- Pair-level crashes: 0 tool-execution notifications, 0 unverified procedures

## Tests (full only)
- Not rerun: the modern side is M4-007's, unchanged. M4-007 recorded legacy 11 passed (net481) and modern 11 passed (net10.0), 0 failed, 0 skipped.
- Passed on legacy, failed on modern: none (M4-007)

## Seeds (seeded, hand-written; the `tools/corpus/seeds.md` catalogue, the same methods as M4-007 because its seeded copies were not kept)
| seed id | procedure identity | verdict | on the seeded line? |
|---|---|---|---|
| S01 | `SignalR.Extras.Autofac.RegisterExtensions::RegisterLifetimeHubManager(Autofac.ContainerBuilder)` | EQ002 Divergent | n/a |
| S02 | `SignalR.Extras.Autofac.LifetimeHubManager::ResolveHub<T>(System.Type,Autofac.ILifetimeScope)` | EQ003 Unknown (abstraction, method-scoped) | yes |
| S03 | `SignalR.Extras.Autofac.LifetimeHubManager::Dispose(bool)` | EQ002 Divergent | n/a |
| S04 | `SignalR.Extras.Autofac.LifetimeHubManager::HubOnDisposing(object,System.EventArgs)` | EQ002 Divergent | n/a |
| S05 | `SignalR.Extras.Autofac.LifetimeHub::Dispose(bool)` | EQ002 Divergent | n/a |
| S06 | `SignalR.Extras.Autofac.LifetimeHub<T>::Dispose(bool)` | EQ002 Divergent | n/a |

- Seeded recall: 6/6 = 100.0%. Blast-radius misses (Unknown that does not point at the seeded line): 0.
- Seeded run totals: EQ001 19, EQ002 5, EQ003 1, EQ004 1, EQ005 0, EQ006 0.

## Mechanical seeds (seeded-mech, ticket M4-010)
- Seeds: requested 300, applied 10, dropped (failed to compile) 0
- **Preserving family** (behaviour unchanged by construction):

| operator | applied | Equivalent (EQ001) | Unknown (EQ003) | Divergent (EQ002 + EQ006) | other or no result |
|---|---|---|---|---|---|
| InlineTemporary | 1 | 1 | 0 | 0 | 0 |
| InvertIf | 2 | 2 | 0 | 0 | 0 |
| RenameLocals | 5 | 4 | 0 | 0 | 1 |
| **all Preserving** | 8 | 7 | 0 | 0 | 1 |

- **Preserving Equivalent share** (`Equivalent / applied`): 7 of 8 = 87.5%. Reported only: ADR 0028 sets no threshold for it.
- **Changing family**:

| operator | applied | Equivalent (EQ001) | Unknown (EQ003) | Divergent (EQ002 + EQ006) | other or no result |
|---|---|---|---|---|---|
| DropFieldWrite | 1 | 0 | 0 | 0 | 1 |
| SwapArguments | 1 | 1 | 0 | 0 | 0 |
| **all Changing** | 2 | 1 | 0 | 0 | 1 |


- Changing family reading (reported only): 0 Divergent, 0 Unknown, 1 Equivalent, 1 no result, of 2.
- Recall = (EQ002 + line-scoped EQ003 on the seed's line) / confirmed behaviour-changing seeds: **not computed**. `seeds.json` records each seed's method start line, not the mutated line, so "on the seed's line" cannot be checked, and no run of the repos' tests confirmed which Changing seeds changed behaviour. The counts above are the reading (P2-063).
- Changing seeds reported Equivalent are not counted as misses. Each was read as a source diff against the unseeded modern side: they are operand swaps of `==`, `!=`, `&&` or an enum-flag `|`, which cannot change behaviour, so they are unconfirmed equivalent mutants (identities only, in `.corpus/`'s `seeds.json`).
- Unconfirmed list size: 1

## Replay (full --execute, M4-009; exit 0)
- Run totals: EQ001 25, EQ002 0, EQ003 0, EQ004 1, EQ005 0, EQ006 0; pair-level crashes 0
- Divergent results with a replay value: 0. By value: n/a; `not-reproduced` 0
- Unknown became Divergent by observation (`proofMethod: observed`): 0
- Differential testing of Unknown pairs: 0 completed, 0 `notConstructible`

## Findings
- No changed pairs, no Unknown, no crash. Hand-written seeds: five Divergent, one Unknown(abstraction); the Unknown points at the seeded line.
- Mechanical: seven Preserving seeds Equivalent; two seeds fall in projects the default configuration does not build (no result); the one Changing seed is `null== builder`, an equivalent mutant.
- No new ticket from this pair.
