# full+seeded run: pmb-lethek__signalr.extras.autofac

- Pair: agent, lethek/SignalR.Extras.Autofac, legacy 3a4ac841ad23, modern agent migration
- Corpus list: Poly-MigrationBench @ pinned commit (see `tools/corpus/README.md`)
- Migrated by: general-purpose agent (this session), 2026-09-27, following `tools/corpus/migration-prompt.md` verbatim (no hints about `equiv`, seeds, or this skill)
- equiv: fd400e9, mode full+seeded+seeded-mech, wall-clock full 7s / seeded 8s / seeded-mech 5s, exit full 0 / seeded 1 / seeded-mech 0

## Load
- Projects: legacy 2/2, modern 2/2; skipped: none
- Project load rate: 100%/100%
- `projectsNotBuilt` (outside default build config): legacy 3 (ExampleUsingOWIN, ExampleUsingIIS, `_build`); modern 2 (ExampleUsingOWIN, `_build`) — the migration removed the `ExampleUsingIIS` project (System.Web/IIS in-process hosting has no .NET 10 equivalent; documented by the migrating agent, not silent)

## Census
| | legacy | modern |
|---|---|---|
| procedures | 25 | 26 |
| analysed lines | 389 | 394 |

- Matched pairs 25; without opaque 20 (80%); whole-body opaque 0 (0%); congruent 25 (100%)
- Unchanged share: 100% (pairsCongruent proxy)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 100% (0 changed pairs)

Top opaque reasons: Conversion 1/1, DelegateCreation 1/1, EventAssignment 4/4 — all owned (Conversion: M2-004/M3-010/M4-002/M4-005; DelegateCreation: M4-004; EventAssignment: P2-004 via `EventReference` row)

## Changed code
- Changed pairs 0 of 25 matched; lowerable share: n/a (no changed pairs)

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 0 | 0 |
| distinct members | 0 | 0 |
| pairs with any | 0 | 0 |

- Package changes: n/a (agent kept Autofac/SignalR.Core at their original versions; both restore fine against net10.0 via NuGet compatibility fallback, NU1701 warnings only)

## Verdicts (full)
- By rule: EQ001 25, EQ004 1 (EQ002/EQ003/EQ005/EQ006: 0)
- By proofMethod: congruence 25, n/a 1
- Unknown by scope: line 0, method 0. Line-scoped Unknown share: n/a (0 Unknown)
- Top Unknown reasons: none (0 Unknown results)

## Tests (full)
- verifyCommand `dotnet test SignalR.Extras.Autofac.sln`, run by me this session: legacy 11 passed, 0 failed, 0 skipped (net481); modern 11 passed, 0 failed, 0 skipped (net10.0)
- Passed on legacy, failed on modern: none

## Seeds (seeded)
| seed id | procedure identity | verdict | on the seeded line? |
|---|---|---|---|
| S05 | `RegisterExtensions::RegisterLifetimeHubManager(ContainerBuilder)` | EQ002 Divergent | n/a |
| S09-1 | `LifetimeHubManager::ResolveHub<T>(Type,ILifetimeScope)` | EQ003 Unknown(abstraction) | no (blast-radius miss) |
| S09-2 | `LifetimeHubManager::Dispose(bool)` | EQ002 Divergent | n/a |
| S09-3 | `LifetimeHubManager::HubOnDisposing(object,EventArgs)` | EQ002 Divergent | n/a |
| S09-4 | `LifetimeHub::Dispose(bool)` | EQ002 Divergent | n/a |
| S09-5 | `LifetimeHub<T>::Dispose(bool)` | EQ002 Divergent | n/a |

- Seeded recall: 6/6 = 100% (ADR 0028/`tools/corpus/seeds.md` rule 5: Unknown counts toward recall regardless of relatedLocation; 1 of 6 is additionally a blast-radius miss)

## Mechanical seeds (M4-010)
- Seeds: requested 300, applied 10, dropped 0
- By verdict: EQ001 (Preserving, correct) 8 [S001,S002,S004,S005,S008(≡S003 fuzzy-matched),S010(≡S006 fuzzy-matched); 2 identities matched only after correcting for the seeder's unqualified-vs-fully-qualified parameter-type-name mismatch, see Findings], out-of-scope (project not built) 2 [S007 ExampleUsingIIS, S009 ExampleUsingOWIN — neither project is in the solution's default build config]
- No `Changing`-family operator was selected by the random draw for this pair, so the EQ002/line-scoped-EQ003 recall metric's denominator is 0: Recall = n/a
- Unconfirmed list size: 0

## Replay (full --execute, M4-009)
- No Divergent or Unknown results in this pair's full run, so nothing to replay or test. By value: n/a (0 of 0)

## Findings
- The mechanical seeder (`tools/corpus/seeder`) records a seeded method's identity with unqualified parameter type names (e.g. `ContainerBuilder`, `EventArgs`) while `equiv`'s own procedure identity is fully qualified (`Autofac.ContainerBuilder`, `System.EventArgs`); 2 of 10 seeds only matched after correcting for this by hand. Not filed as its own ticket — low severity, easy to work around when reading a manifest, and P2-035 already covers a more serious seeder bug.
