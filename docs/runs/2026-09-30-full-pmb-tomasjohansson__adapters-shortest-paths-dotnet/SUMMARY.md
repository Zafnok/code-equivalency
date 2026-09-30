# full+seeded+execute run: pmb-tomasjohansson__adapters-shortest-paths-dotnet

- Pair: agent, TomasJohansson/adapters-shortest-paths-dotnet, legacy e3722e971d86, modern agent migration (M4-007's, reused)
- Corpus list: Poly-MigrationBench @ pinned commit (see `tools/corpus/README.md`)
- Migrated by: general-purpose agent, 2026-09-27 (M4-007), following `tools/corpus/migration-prompt.md` verbatim; reused, not re-migrated
- equiv: bd8e379, wall-clock full 109s exit 1; seeded 131s exit 1; seeded-mech 240s exit 1; full --execute 112s exit 1. Exit 1 means Divergent results exist; no run had a pair-level crash (exit 5) or a load failure (exit 4).

## Phase times
From the `full` run.

| phase | items | seconds | ETA error at 50% |
|---|---|---|---|
| load-legacy | 21 | 0.000 | +0.000 |
| load-modern | 13 | 0.000 | +0.000 |
| enumerate | 2 | 0.067 | n/a |
| match | 1 | 0.002 | n/a |
| lower | 708 | 3.802 | +1.267 |
| verify | 708 | 56.362 | -2.597 |
| write | 1 | 0.077 | +0.000 |

## Load
- Projects: legacy 13 of 13, modern 13 of 13; skipped: none
- Project load rate: 100%

## Census
| | legacy | modern |
|---|---|---|
| procedures | 713 | 709 |
| analysed lines | 7586 | 7439 |

- Matched pairs 708; without opaque 614 (86.7%); whole-body opaque 0 (0.0%); congruent 668 (94.4%)
- Unchanged share: 94.4% (`pairsCongruent` / `matchedPairs`)
- Pair-level unchanged share (1 - changedPairs / matchedPairs): 94.4%. Not the row above; ADR 0034.

Top opaque reasons (legacy / modern), up to 15:

| reason | legacy | modern | owning ticket or "none" |
|---|---|---|---|
| Binary | 48 | 48 | P1-014 (ADR 0039) |
| DelegateCreation | 24 | 24 | none open (M4-004 shares identical fragments; this run files P2-067) |
| Conversion | 10 | 10 | P1-014 (ADR 0039) |
| DefaultValue | 5 | 5 | none (0.7%, below 5%) |
| CaughtException | 3 | 3 | none (1.1%, below 5%) |
| CompoundAssignment | 2 | 2 | P1-014 (ADR 0039) |
| TranslatedQuery | 2 | 2 | none (0.0% alone) |

## Changed code
- Changed pairs 40 of 708; without opaque 29; whole-body opaque 0
- Lowerable share (changedPairsWithoutOpaque / changedPairs): 72.5%

Top reason sets (up to 15; "" = no opaque):

| reason set | changed pairs | owning tickets or "none" |
|---|---|---|
| "" | 29 | n/a |
| "Binary" | 7 | P1-014 (ADR 0039) |
| "CaughtException" | 2 | none (1.1%, below 5%) |
| "Conversion" | 1 | P1-014 (ADR 0039) |
| "DelegateCreation" | 1 | none open (M4-004 shares identical fragments; this run files P2-067) |

| runtime-change calls | legacy | modern |
|---|---|---|
| call sites | 34 | 34 |
| distinct members | 12 | 12 |
| pairs with any | 22 | 22 |

- Package changes: 2 version changed, 1 legacy only, 20 modern only: netstandard.library 2.0.3 -> 2.0.0; nhibernate 5.2.7 -> 5.5.2

## Verdicts (full, no --execute)
- By rule: EQ001 681, EQ002 1, EQ003 17, EQ004 1, EQ005 5, EQ006 16
- By proofMethod: bounded 11, congruence 668, lockstep-induction 2, n/a 40; solver-proved Equivalent on changed pairs (`proofMethod` other than `congruence`): 13
- Unknown by scope: line 1, method 16. Line-scoped Unknown share: 5.9% (1 of 17)
- Top Unknown reasons: unmatched-overload 7, abstraction 4, opaque 3, timeout 2, unaligned-loop 1
- Top abstractions (4 `abstraction` Unknown results; entries of `properties.abstractions` by kind, top 15; `opaque` is one kind because the SARIF records a fragment's fingerprint and not its reason, and 2 distinct fragments are behind it): f64.mul 4, opaque 3, conv.f64.i32 2, conv.i32.f64 2
- Pair-level crashes: 0 tool-execution notifications, 0 unverified procedures

## Tests (full only)
- Not rerun: the modern side is M4-007's, unchanged. M4-007 recorded modern 98 passed, 0 failed, 0 skipped; no legacy count.
- Passed on legacy, failed on modern: none (M4-007)

## Seeds (seeded, hand-written; the `tools/corpus/seeds.md` catalogue, the same methods as M4-007 because its seeded copies were not kept)
| seed id | procedure identity | verdict | on the seeded line? |
|---|---|---|---|
| S01 | `Programmerare.ShortestPaths.Utils.MapperForIntegerIdsAndGeneralStringIds::.ctor(int)` | EQ002 Divergent | n/a |
| S02 | `Programmerare.ShortestPaths.Core.Impl.WeightImpl::ToString()` | EQ003 Unknown (opaque, line-scoped) | yes |
| S03 | `Programmerare.ShortestPaths.Utils.SelectionStrategySmallestWeight<E,V,W>::Reduce(System.Collections.Generic.IList<E>)` | EQ003 Unknown (abstraction, method-scoped) | no (blast-radius miss) |
| S04 | `Programmerare.ShortestPaths.Core.Validation.GraphEdgesValidator<P,E,V,W>::ValidateNonNullObjects(E)` | EQ002 Divergent | n/a |
| S05 | `Programmerare.ShortestPaths.Core.Impl.Generics.PathGenericsImpl<E,V,W>::IsAnyVertexMismatching<E,V,W>(System.Collections.Generic.IList<E>)` | EQ002 Divergent | n/a |
| S06 | `Programmerare.ShortestPaths.Core.Impl.Generics.GraphGenericsImpl<E,V,W>::get_Vertices()` | EQ003 Unknown (timeout, method-scoped) | no (blast-radius miss) |
| S07 | `Programmerare.ShortestPaths.Core.Impl.Generics.EdgeGenericsImpl<V,W>::.ctor(string,V,V,W)` | EQ002 Divergent | n/a |

- Seeded recall: 7/7 = 100.0%. Blast-radius misses (Unknown that does not point at the seeded line): 2.
- Seeded run totals: EQ001 674, EQ002 5, EQ003 20, EQ004 1, EQ005 5, EQ006 16.

## Mechanical seeds (seeded-mech, ticket M4-010)
- Seeds: requested 300, applied 92, dropped (failed to compile) 0
- **Preserving family** (behaviour unchanged by construction):

| operator | applied | Equivalent (EQ001) | Unknown (EQ003) | Divergent (EQ002 + EQ006) | other or no result |
|---|---|---|---|---|---|
| Commute | 5 | 2 | 3 | 0 | 0 |
| InlineTemporary | 15 | 14 | 0 | 1 | 0 |
| IntroduceTemporary | 12 | 10 | 0 | 2 | 0 |
| InvertIf | 8 | 7 | 0 | 1 | 0 |
| RenameLocals | 22 | 20 | 2 | 0 | 0 |
| ReorderIndependentStatements | 1 | 1 | 0 | 0 | 0 |
| **all Preserving** | 63 | 54 | 5 | 4 | 0 |

- **Preserving Equivalent share** (`Equivalent / applied`): 54 of 63 = 85.7%. Reported only: ADR 0028 sets no threshold for it.
- **Changing family**:

| operator | applied | Equivalent (EQ001) | Unknown (EQ003) | Divergent (EQ002 + EQ006) | other or no result |
|---|---|---|---|---|---|
| ChangeConstant | 1 | 0 | 0 | 1 | 0 |
| DropFieldWrite | 10 | 0 | 3 | 7 | 0 |
| FlipComparison | 6 | 0 | 2 | 4 | 0 |
| SwapArguments | 12 | 3 | 6 | 3 | 0 |
| **all Changing** | 29 | 3 | 11 | 15 | 0 |


- Changing family reading (reported only): 15 Divergent, 11 Unknown, 3 Equivalent, 0 no result, of 29.
- Recall = (EQ002 + line-scoped EQ003 on the seed's line) / confirmed behaviour-changing seeds: **not computed**. `seeds.json` records each seed's method start line, not the mutated line, so "on the seed's line" cannot be checked, and no run of the repos' tests confirmed which Changing seeds changed behaviour. The counts above are the reading (P2-063).
- Changing seeds reported Equivalent are not counted as misses. Each was read as a source diff against the unseeded modern side: they are operand swaps of `==`, `!=`, `&&` or an enum-flag `|`, which cannot change behaviour, so they are unconfirmed equivalent mutants (identities only, in `.corpus/`'s `seeds.json`).
- Unconfirmed list size: 3

## Replay (full --execute, M4-009; exit 1)
- Run totals: EQ001 681, EQ002 2, EQ003 16, EQ004 1, EQ005 5, EQ006 16; pair-level crashes 0
- Divergent results with a replay value: 17. By value: `not-constructible` 17; `not-reproduced` 0
- `not-constructible` reasons: call trace 7, not public 6, no public parameterless constructor 2, other: legacy: generic 1, heap map, cast, typeof or field 1
- Unknown became Divergent by observation (`proofMethod: observed`): 1
- Differential testing of Unknown pairs: 1 completed, 8 `notConstructible`

## Findings
- No crash (M4-007 had one, P2-033) and `--execute` finished in under two minutes (M4-007 hung, P2-039).
- Mechanical seeds now run on this pair (M4-007: the seeder crashed, P2-035): 92 applied. No Preserving seed turned Divergent because of the seed; four Preserving seeds are EQ006, and each was EQ006 without any seed (the method already calls a runtime-changed member).
- Three Changing `SwapArguments` seeds are Equivalent; each is a commutative operand swap (`id == null`, `&&` operands, `Count == 0`), so unconfirmed equivalent mutants, not misses.
- No new ticket from this pair.
