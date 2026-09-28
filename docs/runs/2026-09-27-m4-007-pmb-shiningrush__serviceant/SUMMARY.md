# full+seeded run: pmb-shiningrush__serviceant

- Pair: agent, ShiningRush/ServiceAnt, legacy e36009c2ee86, modern agent migration
- Corpus list: Poly-MigrationBench @ pinned commit (see `tools/corpus/README.md`)
- Migrated by: general-purpose agent (this session), 2026-09-27, following `tools/corpus/migration-prompt.md` verbatim
- equiv: fd400e9, mode full+seeded+seeded-mech, wall-clock full 35s / seeded 50s / seeded-mech 35s, exit full 5 / seeded 5 / seeded-mech 5 (all three: one recurring engine crash, not a run failure — see Findings)

## Load
- Projects: legacy 6/6, modern 6/6; skipped: none (legacy side needed an MSBuild `-t:restore -p:RestorePackagesConfig=true` pass first — its test projects use classic `packages.config`, which `dotnet restore` does not handle; recorded as a toolchain note, not a finding)
- Project load rate: 100%/100%

## Census
| | legacy | modern |
|---|---|---|
| procedures | 136 | 137 |
| analysed lines | 1532 | 1534 |

- Matched pairs 136; without opaque 93 (68.4%); whole-body opaque 0 (0%); congruent 113 (83.1%)
- Unchanged share: 83.1% (pairsCongruent proxy)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 83.1%

Top opaque reasons: DelegateCreation 27/27 (M4-004), Await 3/3 (M3-026/M4-006), Conversion 6/6, TypeOf 6/6, Binary 4/4, CaughtException 2/2, DefaultValue 2/2, EventAssignment 1/1 — all owned

## Changed code
- Changed pairs 23 of 136 matched; without opaque 4; lowerable share (changedPairsWithoutOpaque/changedPairs): 17.4%
- Reason sets: DelegateCreation 16, (no opaque) 4, Await+CaughtException+Conversion+DefaultValue 1, Conversion+DelegateCreation 1, Await+CaughtException 1

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 2 | 2 |
| distinct members | 1 | 1 |
| pairs with any | 2 | 2 |

## Verdicts (full)
- By rule: EQ001 113, EQ002 4, EQ003 18, EQ004 1 (EQ005/EQ006: 0)
- By proofMethod: congruence 113, n/a 23
- Unknown by scope: line 9, method 9. Line-scoped Unknown share: 50%
- Top Unknown reasons: opaque 11, abstraction 7

## Tests (full)
- verifyCommand `dotnet test ServiceAnt.sln`, run by me this session: modern 21 passed, 0 failed, 0 skipped (17 + 2 + 2 across three test assemblies, net10.0). Legacy: `dotnet test` on the old-style test projects printed no result, so there is no legacy count from this session (the pinned Poly-MigrationBench commit is stated to pass, ADR 0028; not re-verified)
- Passed on legacy, failed on modern: none (no modern test fails)

## Seeds (seeded)
| seed id | procedure identity | verdict | on the seeded line? |
|---|---|---|---|
| S07-1 | `ServiceAntInstaller::Kernel_ComponentRegistered(string,IHandler)` | EQ003 Unknown | no (blast-radius miss) |
| S07-2 | `ServiceAntModule::RegisterHandlerType(IComponentContext,Type)` | EQ003 Unknown | no (blast-radius miss) |
| S07-3 | `InProcessServiceBus::ProcessRequest<T>(string,string)` | EQ003 Unknown | no (blast-radius miss) |
| S09-1 | `SingletonHandlerFactory::.ctor(IHandler,Type)` | EQ002 Divergent | n/a |
| S09-2 | `TransportTray<TEntity>::.ctor(TEntity)` | EQ002 Divergent | n/a |
| S09-3 | `IocHandlerFactory::.ctor(IIocResolver,Type,Type)` | EQ002 Divergent | n/a |
| S09-4 | `InMemorySubscriptionsManager::.ctor()` | EQ002 Divergent | n/a |

- Seeded recall: 7/7 = 100% (ADR 0028/`tools/corpus/seeds.md` rule 5: Unknown counts toward recall regardless of relatedLocation; 3 of 7 are additionally blast-radius misses)

## Mechanical seeds (M4-010)
- Seeds: requested 300, applied 13, dropped 0
- By verdict: EQ001 (Preserving, correct) 8 [S004,S007,S008,S009,S011,S012,S013, and one of the two S010 generic overloads], EQ001 (Preserving, WRONG — see Findings and P2-036) 1 [S003], EQ002 (Changing, confirmed miss — behaviour-changing operator correctly caught) 1 [S006 DropFieldWrite], EQ003 line-scoped 0, EQ003 other (Changing, unconfirmed) 2 [S002, S005 FlipComparison — both Unknown/opaque near but not exactly on the seeded line; not verified against `verifyCommand`/replay this session]
- Recall = (EQ002 + line-scoped EQ003 on the seed's line) / confirmed behaviour-changing seeds = 1/1 = 100% (S006 only; S002/S005 are unconfirmed, not counted in either numerator or denominator)
- Unconfirmed list size: 2

## Replay (full --execute, M4-009)
- By value: `not-constructible` 4 (all 4 EQ002 Divergent; reason: "the divergence is in the call trace, which replay does not observe" — a call-order/trace difference, not a value the driver observes)
- Testing (Unknown pairs): 16 completed (1000 inputs each, 1 species, discoveryProbability 0.0 — stable, no new behaviour surfaced); 2 `notConstructible` ("legacy: not public")
- No `not-reproduced` result (would be a soundness/modelling finding) — none occurred

## Findings
- **Engine crash, reproducible, all three modes**: `Verifying YiBan.Common.BaseAbpModule.Tests.Events.InProcessServiceBus_Test::GenericRequest_ShouldResponse() against ...(same)... failed: domain sort |YiBan.Common.BaseAbpModule.Tests.Events.InProcessServiceBus_Test+TestEventDataT`1| and parameter |ServiceAnt.Handler.TransportTray`1| do not match`. Filed as P2-031 (the same domain-sort-mismatch family as Git Extensions' 40+ occurrences).
- **Precision bug**: mechanical seed S003 (`RenameLocals`, Preserving family, on `ServiceAntInstaller_Test::CanHandleEventByIocHandler()`) came back Divergent with a counterexample over `IWindsorInstaller[]` — a rename can never change behaviour, so this is `equiv` wrongly disagreeing. Filed as P2-036 (possibly the same root cause as P2-031/P2-032, noted there).
