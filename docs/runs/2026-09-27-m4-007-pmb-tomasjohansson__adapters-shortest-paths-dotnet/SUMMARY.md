# full+seeded run: pmb-tomasjohansson__adapters-shortest-paths-dotnet

- Pair: agent, TomasJohansson/adapters-shortest-paths-dotnet, legacy e3722e971d86, modern agent migration
- Corpus list: Poly-MigrationBench @ pinned commit (see `tools/corpus/README.md`)
- Migrated by: general-purpose agent (this session), 2026-09-27, following `tools/corpus/migration-prompt.md` verbatim
- equiv: fd400e9, mode full+seeded, wall-clock full ~60s / seeded 73s, exit full 5 / seeded 5 (one recurring engine crash, not a run failure — see Findings). Mechanical seeding crashed before producing a run (see Findings; P2-035).

## Load
- Projects: legacy 13/13, modern 13/13; skipped: none
- Project load rate: 100%/100%

## Census
| | legacy | modern |
|---|---|---|
| procedures | 713 | 709 |
| analysed lines | 7586 | 7439 |

- Matched pairs 708; without opaque 610 (86.2%); whole-body opaque 0 (0%); congruent 668 (94.4%)
- Unchanged share: 94.4% (pairsCongruent proxy)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 94.4%

Top opaque reasons: Binary 48/48, DelegateCreation 24/24, Conversion 10/10, DefaultValue 5/5, FlowCaptureReference 5/5, CaughtException 3/3, CompoundAssignment 2/2, TranslatedQuery 2/2 — all owned except TranslatedQuery (P2-026, new)

## Changed code
- Changed pairs 40 of 708 matched; without opaque 29; lowerable share (changedPairsWithoutOpaque/changedPairs): 72.5%
- Reason sets: (no opaque) 29, Binary 7, CaughtException 2, DelegateCreation 1, Conversion 1

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 34 | 34 |
| distinct members | 12 | 12 |
| pairs with any | 22 | 22 |

- Package changes: NHibernate 5.2.7 -> 5.5.2 (migrating agent's fix for a .NET 10 proxy-validation incompatibility, `Programmerare.ShortestPaths.Example` only); no other version changes

## Verdicts (full)
- By rule: EQ001 681, EQ002 1, EQ003 17, EQ004 1, EQ005 5, EQ006 15
- By proofMethod: bounded 11, congruence 668, lockstep-induction 2, n/a 39
- Unknown by scope: line 1, method 16. Line-scoped Unknown share: 5.9%
- Top Unknown reasons: unmatched-overload 7, abstraction 4, opaque 3, unaligned-loop 2, timeout 1

## Tests (full)
- verifyCommand `dotnet test adapters-shortest-paths-dotnet.sln`, run by me this session: modern 98 passed, 0 failed, 0 skipped (net10.0). Legacy: `dotnet test` on the old-style test projects printed no result, so there is no legacy count from this session (the pinned Poly-MigrationBench commit is stated to pass, ADR 0028; not re-verified)
- Passed on legacy, failed on modern: none (no modern test fails)

## Seeds (seeded)
| seed id | procedure identity | verdict | on the seeded line? |
|---|---|---|---|
| S07-mapper-offset | `MapperForIntegerIdsAndGeneralStringIds::.ctor(int)` | EQ002 Divergent | n/a |
| S10-weightimpl-tostring-culture | `WeightImpl::ToString()` | EQ003 Unknown(opaque) | yes |
| S03-edgeutility-smallestweight-bound | `SelectionStrategySmallestWeight::Reduce(IList<E>)` | EQ003 Unknown(abstraction) | no (blast-radius miss) |
| S05-graphedgesvalidator-weight-guard | `GraphEdgesValidator::ValidateNonNullObjects(E)` | EQ002 Divergent | n/a |
| S03-pathgenericsimpl-vertexmismatch-bound | `PathGenericsImpl::IsAnyVertexMismatching(IList<E>)` | EQ002 Divergent | n/a |
| S06-graphgenericsimpl-vertex-order-swap | `GraphGenericsImpl::get_Vertices()` | EQ003 Unknown(timeout) | no (blast-radius miss) |
| S09-edgegenericsimpl-id-write-deleted | `EdgeGenericsImpl::.ctor(string,V,V,W)` | EQ002 Divergent | n/a |

- Seeded recall: 7/7 = 100% (ADR 0028/`tools/corpus/seeds.md` rule 5: Unknown counts toward recall regardless of relatedLocation; 2 of 7 are additionally blast-radius misses)

## Mechanical seeds (M4-010)
- Seeds: requested 300, applied 0, dropped 0 — **the seeder itself crashed** (`System.InvalidOperationException: The item specified is not the element of a list`) immediately after building the weighted-selection changed-identity list, before selecting or applying any mutation, at both `--seed 1` and `--seed 2`. Filed as P2-035.
- Recall: n/a (no seeds applied)

## Replay (full --execute, M4-009)
- **The `--execute` run hung and produced no output.** About a minute in, its two replay drivers (project `Programmerare.ShortestPaths.Adaptee.YanQi`) went idle and stayed idle for hours (0.03 CPU-s each; the parent at 65 CPU-s), and the documented 60 s per-pair budget never fired. P2-039.
- By value: n/a (no result was written). The plain `full` run above has 1 Divergent (EQ002) and 15 EQ006 that a working `--execute` would have replayed, and 17 Unknown it would have tested.

## Findings
- **Engine crash, reproducible, both full and seeded runs, unchanged by seeding**: `Verifying Programmerare.ShortestPaths.Adaptee.YanQi.Test.YenTopKShortestPathsAlgTest::GetExpectedWeightAndNodes(string) against ...(same)... failed: The given key 'IrSortValue { Type = IrSort { Name = System.String }, Sort = System.String, Id = 1174359459 }' was not present in the dictionary.` Filed as P2-033.
- **Tooling crash**: `tools/corpus/seeder` (M4-010) crashes on this pair before applying any mechanical seed. Filed as P2-035.
- **Hang**: `compare --execute` deadlocks on this pair. Filed as P2-039.
- `TranslatedQuery` (2 occurrences) has no owning ticket — filed as P2-026, which also covers a related frontend crash found on Git Extensions.
