# P2-046 verdict: second full corpus run (2026-09-29 to 2026-09-30)

Same four pairs as M4-007 (`docs/runs/2026-09-27-m4-007-verdict.md`), same modern sides, equiv
`bd8e379` (41 commits after M4-007's `fd400e9`). Thresholds are ADR 0028's, applied as written.
Per-pair detail is in `docs/runs/2026-09-30-full-<slug>/SUMMARY.md`. Nothing under `src/` or
`tests/` was changed.

## Metrics

| Pair | Kind | Unchanged share | Pair-level figure (1 - changedPairs / matchedPairs) | Lowerable share (changed pairs) | Project load rate | Line-scoped Unknown share | Seeded recall (hand-written) | Mechanical seeds: Preserving Equivalent share (reported only) |
|---|---|---|---|---|---|---|---|---|
| gitextensions-8522 | human | 91.6% | 91.6% | 39.1% (447 of 1143) | 100% | 20.0% (145 of 726) | 100% (8 of 8) | 70.1% (136 of 194) |
| pmb-shiningrush__serviceant | agent | 98.5% | 98.5% | 0% (0 of 2) | 100% | 0% (0 of 2) | 100% (7 of 7) | 100% (9 of 9) |
| pmb-lethek__signalr.extras.autofac | agent | 100% | 100% | n/a (no changed pairs) | 100% | n/a (no Unknown) | 100% (6 of 6) | 87.5% (7 of 8) |
| pmb-tomasjohansson__adapters-shortest-paths-dotnet | agent | 94.4% | 94.4% | 72.5% (29 of 40) | 100% | 5.9% (1 of 17) | 100% (7 of 7) | 85.7% (54 of 63) |
| **Agent median** | | **98.5%** | 98.5% | see below | 100% | 2.9% (two pairs) | 100% | n/a |

The unchanged-share column is `pairsCongruent / matchedPairs`; the next is the pair-level figure of
ADR 0034 and, since a matched pair is either congruent or changed, the two coincide here. They are
different definitions and are labelled apart.

Lowerable-share median: lethek has no changed pairs and is left out, leaving serviceant and Tomas,
fewer than three. Per the skill, the lowerable-share rules are applied to Git Extensions alone
(39.1%). The unchanged-share rule still uses the agent median.

Mechanical seed recall is not computed: `seeds.json` records the method's first line, not the
mutated line, so "on the seed's line" cannot be checked, and no test run confirmed which Changing
seeds changed behaviour (P2-063). Reading the Changing seeds: Git Extensions 36 Divergent, 62 Unknown,
5 Equivalent, 3 without a result, of 106. All 5 Equivalent are commutative operand swaps, read from
the source diffs, so they are equivalent mutants and not misses. The agent pairs: Tomas 15 Divergent,
11 Unknown, 3 Equivalent (swaps) of 29; ServiceAnt 3 Unknown and 1 without a result of 4; lethek 1
Equivalent (a swap) and 1 without a result (its project is not built) of 2.

## Rule outcomes

| Rule (ADR 0028) | Git Extensions | Agent median | Outcome |
|---|---|---|---|
| Unchanged share below 40%: stop | 91.6% | 98.5% | Not triggered |
| Lowerable share: the top three M4 tickets must together lift it to 15% | 39.1%, already above 15% | not evaluated (fewer than 3 pairs) | Not triggered |
| Project load rate below 100% on a human pair is a ticket | 100% | 100% | Not triggered |
| Line-scoped Unknown share | 20.0% (method-scoped 80.0%) | 2.9% | Reported. The method-scoped share is the one that must fall with each M4 ticket |
| Seeded recall must be 100% | 8 of 8 | 20 of 20 | Met. 28 of 28 hand-written seeds Divergent or Unknown, none Equivalent. Blast-radius misses (Unknown not on the seeded line): 2 + 0 + 3 + 2 = 7 of 28 |

## Since M4-007

M4-007's value next to this run's. Git Extensions unless the row says otherwise. Both runs used the
same pair, the same modern sides and the same procedure.

| Measure | M4-007 (`fd400e9`) | This run (`bd8e379`) |
|---|---|---|
| Unchanged share (ADR 0028) | 91.1% | 91.6% |
| Unchanged share, agent median | 94.4% | 98.5% |
| Pair-level figure | 91.2% | 91.6% |
| Lowerable share (changed pairs) | 37.3% (447 of 1197) | 39.1% (447 of 1143) |
| Project load rate | 100% | 100% |
| Line-scoped Unknown share | 20.3% (142 of 700) | 20.0% (145 of 726) |
| Line-scoped Unknown share, agent median | 27.9% | 2.9% |
| Seeded recall, hand-written | 100% (28 of 28) | 100% (28 of 28) |
| Seeded recall, mechanical | n/a (seeder crashed, P2-035) | not computed (P2-063); no Changing seed reported Equivalent that changes behaviour |
| EQ001 Equivalent | 12414 | 12475 |
| EQ002 Divergent | 82 | 84 |
| EQ003 Unknown | 700 | 726 |
| EQ004 | 15 | 15 |
| EQ005 | 167 | 167 |
| EQ006 Divergent (runtime change) | 272 | 275 |
| Pair-level crashes (all four pairs) | 94 (92 Git Extensions, 1 ServiceAnt, 1 Tomas; exit 5) | 0 (exit 1 on every run that has a Divergent result) |
| Pair-level crashes, Git Extensions | 92 | 0 |
| Solver-proved Equivalent on changed pairs (`proofMethod` other than `congruence`) | 73 (bounded 61, lockstep-induction 12) | 77 (bounded 64, lockstep-induction 13) |
| Unknown by reason: abstraction | 261 | 238 |
| Unknown by reason: timeout | 161 | 206 |
| Unknown by reason: opaque | 197 | 193 |
| Unknown by reason: unaligned-loop | 58 | 66 |
| Unknown by reason: unmatched-overload | 19 | 19 |
| Unknown by reason: recursion | 4 | 4 |
| Congruent pairs / changed pairs | 12341 / 1197 | 12398 / 1143 |
| `--execute`: Divergent results replayed | 352 | 362 |
| `--execute`: `reproduced` / `not-reproduced` | 5 / 16 | 4 / 0 |
| `--execute`: Unknown became Divergent by observation | 54 | 55 |
| `--execute`: pair-level crash or hang | Tomas hung (P2-039) | none; Tomas `--execute` finished in 112s |
| Wall-clock, `full` | 9450s (2h37m), run alone | 32211s (8h57m), four runs at once |

Reading it:
- **The crashes are gone.** 92 pairs on Git Extensions and one each on two smaller pairs became 0.
  Their verdicts moved into the rows above: EQ001 +61, EQ003 +26, EQ002 +2, EQ006 +3.
- **Both `--execute` findings closed.** No hang, and no `not-reproduced` replay (16 before: P2-037,
  P2-038). Execution still turns Unknown into Divergent, 55 times, so the second oracle's value is
  unchanged. The 55 include production code, for example `DpiUtil::Scale(int)`,
  `GitDirectoryResolver::Resolve(string)`, `AppSettings::GetResourceDir()`.
- **ServiceAnt changed the most.** Congruent pairs went from 113 to 134 of 136 (83.1% to 98.5%),
  changed pairs from 23 to 2, Unknown from 18 to 2. On Git Extensions congruent pairs rose by 57 and
  changed pairs fell by 54 (1197 to 1143), while the no-opaque count of changed pairs stayed 447, so
  the lowerable share rose only because its denominator fell.
- **Timeouts are not comparable.** Timeout Unknowns rose from 161 to 206 while abstraction fell from
  261 to 238. This run's four Git Extensions runs shared one machine, and the solver budget is
  wall-clock (P2-050), so contention alone can move that row. The total also includes 26 pairs that
  used to crash. Do not read the timeout row as a regression until P2-050.
- **Nondeterminism persists.** Between the plain and `--execute` runs of the same inputs, 5 results
  changed rule without an observed proof (4 Unknown to EQ006, 1 EQ006 to Unknown); M4-007 saw 2.

## Per-ticket unlock table (ADR 0034 item 2), recomputed from this run

Git Extensions, 1,143 changed pairs. "Alone" is the pairs whose reason set is exactly that reason:
removing it makes them lowerable, exact and not an upper bound. "In" is the pairs whose set
contains it. The 5% rule is ADR 0028's: a reason at or above 5% of changed pairs needs an owner.

| Reason | Changed pairs it is in | Alone | Alone, share of changed pairs | Owner | Outcome |
|---|---|---|---|---|---|
| DelegateCreation | 328 | 194 | 17.0% | none open (M4-004 done: shares identical fragments only) | **P2-060 filed** |
| switch-pattern | 203 | 79 | 6.9% | P1-014, P1-015, P1-016 (ADR 0039 IL fallback) | owned |
| Binary | 105 | 39 | 3.4% | ADR 0039 | below 5% |
| Conversion | 100 | 22 | 1.9% | ADR 0039 | below 5% |
| InterpolatedString | 91 | 26 | 2.3% | ADR 0039 | below 5% |
| DefaultValue | 53 | 8 | 0.7% | none | below 5% |
| CaughtException | 42 | 13 | 1.1% | none | below 5% |
| DeconstructionAssignment | 25 | 6 | 0.5% | ADR 0039 | below 5% |
| CompoundAssignment | 21 | 9 | 0.8% | ADR 0039 | below 5% |
| Tuple | 20 | 6 | 0.5% | none | below 5% |
| iterator | 14 | 14 | 1.2% | none | below 5% |
| ArrayCreation | 13 | 8 | 0.7% | none | below 5% |
| ArrayElementReference | 10 | 5 | 0.4% | none | below 5% |
| undefined | 10 | 1 | 0.1% | none | below 5% |
| call-throw-in-try | 6 | 2 | 0.2% | none | below 5% |
| TypeOf | 5 | 2 | 0.2% | none | below 5% |
| InstanceReference | 4 | 3 | 0.3% | none | below 5% |
| TranslatedQuery | 4 | 0 | 0.0% | P2-026 (done) | below 5% |
| PropertyReference, ref-argument, rethrow | 3, 2, 2 | 1, 1, 1 | 0.1% each | none | below 5% |
| Discard, Throw, DynamicInvocation, SizeOf, SimpleAssignment | 3, 2, 1, 1, 1 | 0 | 0.0% | none | below 5% |

Cumulative, by the ADR's rule (pairs whose whole reason set is removed), in this order:

| After removing | Changed pairs unlocked | Share of changed pairs | Added |
|---|---|---|---|
| DelegateCreation | 194 | 17.0% | 194 |
| + switch-pattern | 307 | 26.9% | 113 |
| + Binary | 364 | 31.8% | 57 |
| + Conversion | 419 | 36.7% | 55 |
| + InterpolatedString | 481 | 42.1% | 62 |
| + CaughtException | 515 | 45.1% | 34 |
| + iterator | 529 | 46.3% | 14 |

Adding those 529 to the 447 already lowerable, the lowerable share would be 85.4% (976 of 1143).
The eight reasons M4-007 filed tickets for (P2-023 to P2-030) are all still present, in small numbers:
`AddressOf` 1, `AnonymousObjectCreation` 6, `TypeParameterObjectCreation` 2, `DynamicInvocation` 1,
`SizeOf` 1, `TranslatedQuery` 8 bodies. The two larger ones barely moved: `DeconstructionAssignment`
is unchanged at 80 bodies and `Tuple` fell from 54 to 48. Each is about 0.5% of changed pairs alone,
so this run neither needs nor files a ticket, but the owners of P2-025 and P2-027 should check that
the forms Git Extensions uses are the forms those tickets lowered. Also in the census: `ArrayCreation`
42, `ref-argument` 26, `undefined` 26, `call-throw-in-try` 13.

## Findings

Every new crash, `not-reproduced` replay and Preserving seed reported Divergent is a ticket
(M4-007 criterion 6). There are no crashes and no `not-reproduced`.

- **P2-060** `DelegateCreation` is 17.0% of changed pairs on its own and has no open owner.
- **P2-061** Three Preserving seeds turned an Equivalent result into Divergent (S150, S177, S186 on
  Git Extensions). Four other Preserving seeds are Divergent, but their methods already were without
  any seed. Ten Preserving seeds are EQ006 (runtime change; six Git Extensions, four Tomas); each was
  EQ006 unseeded too.
- **P2-062** The SARIF does not say why an opaque fragment is opaque, so "Top abstractions" cannot
  group its 503 opaque entries (of 743) by reason. Grouped by what it can group: `conv.i32.f32` 27,
  `System.String::op_Equality` 22, `conv.f64.i32` 21, `f32.mul` 20, `conv.f32.i32` 19.
- **P2-063** Corpus tooling: `seeds.json` records the method's first line; `-Metrics` reads a
  property that is not there; two runs on one checkout collide (below).
- No soundness finding. No hand-written seed is Equivalent. Of 141 mechanical Changing seeds (106
  Git Extensions, 4 ServiceAnt, 2 lethek, 29 Tomas), 9 were reported Equivalent and none of the 9
  changes behaviour: each is a commutative operand swap.

## Method notes

- Run at `bd8e379`. The mechanical seeder no longer crashes (P2-035): 300 requested and applied on
  Git Extensions (17 dropped for not compiling), 92 on Tomas, 13 on ServiceAnt, 10 on lethek.
- The hand-written seed copies of M4-007 were not kept, so the 28 seeds were re-applied to the
  same methods, one catalogue change each.
- **A run pair was void and rerun.** The first `full` and `full --execute` on Git Extensions were
  started in the same second on the same checkouts. Their MSBuild loads collided on
  `obj/**/*.AssemblyReference.cache`, so 17 legacy and 14 modern projects were skipped and
  `--execute` exited 4. Both were discarded and rerun one after the other; the rerun loaded every
  project. The `seeded` and `seeded-mech` runs, on their own copies, were unaffected. Nothing from
  the void pair is in these numbers. P2-063 asks the skill to say so.
- **Concurrency.** The four Git Extensions runs ran at once on a 24-core box (each solver is
  single-threaded), taking 9h to 12h27m; M4-007's took 2h37m to 2h48m each, alone. Each spent hours
  in one or two GUI-form methods (`InitializeComponent`) in verify. No run was stopped early.
- The agent pairs' modern sides were reused as they were (no new agent migration), and their tests
  were not rerun; the counts in each SUMMARY are M4-007's.

## Verdict: **continue**

Nothing in ADR 0028's table triggers a stop or a re-scope, and every rate that the 2026-09-28
assessment quoted as stale is now measured: crashes 92 to 0, unchanged share 91.6% (agent median
98.5%), lowerable share 39.1%, line-scoped Unknown share 20.0% on Git Extensions, seeded recall 100%
with no behaviour-changing seed called Equivalent, 70.1% of Preserving mechanical seeds Equivalent.
The largest single lever on the changed-pair population is `DelegateCreation`, at 17.0%, which now has
a ticket.
