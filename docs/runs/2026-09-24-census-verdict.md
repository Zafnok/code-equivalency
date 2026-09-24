# Census verdict, 2026-09-24 (M3-031)

ADR 0028's rules, as amended by ADR 0034, applied to the `census` runs of 2026-09-24. equiv at
51fd745; Poly-MigrationBench list @ b0a91412d64a14b7fd61319591bd4b00a574f4c2 (unchanged from
2026-09-23). Per-pair detail is in the four `2026-09-24-census-*/SUMMARY.md` files. This verdict
takes the place M3-022's had (`docs/runs/2026-09-23-census-verdict.md`, "incomplete: human pair
pending"): it is the feasibility test.

## Pairs and selection
Same pairs M3-022 used, per M3-031 criterion 1. No new agent migration; the three agent pairs'
existing modern sides in `.corpus/` were reused as-is.
- Human pair: `gitextensions-8522`.
- Agent pairs: `pmb-shiningrush__serviceant`, `pmb-lethek__signalr.extras.autofac`,
  `pmb-tomasjohansson__adapters-shortest-paths-dotnet` (in place of `chrismckelt/WebMinder`,
  skipped by M3-022 because its migration does not build).

## Metrics

| Pair | Unchanged share | Pair-level unchanged (1-changedPairs/matchedPairs) | Lowerable share | Project load rate | Line-scoped Unknown share | Seeded recall |
|---|---|---|---|---|---|---|
| gitextensions-8522 (human) | 74.0% | 97.7% | 24.4% (77/315, changed pairs) | 100% / 100% | n/a (census) | n/a (census) |
| pmb-shiningrush__serviceant | 100% | 100% | n/a: no changed pairs | 100% / 100% | n/a | n/a |
| pmb-lethek__signalr.extras.autofac | 100% | 100% | n/a: no changed pairs | 100% / 100% | n/a | n/a |
| pmb-tomasjohansson__adapters-shortest-paths-dotnet | 100% | n/a (matchedPairs 0) | n/a: no changed pairs | 53.8% / 100%\* | n/a | n/a |
| **Agent median** | **100%** | **100%** | **n/a (0 of 3 agent pairs have changed pairs)** | **100% / 100%** | n/a | n/a |

\* ShortestPaths' modern-side load rate is nominal only: `projectsSkipped` is 0, but the run
extracted 0 modern procedures against 312 matched pairs in the 2026-09-23 run on the same commit.
See its SUMMARY's Findings and P2-016. It does not change any rule outcome below, since this pair
has zero changed pairs either way.

## Rules

- **Unchanged share below 40%: stop.** Git Extensions 74.0%, agent median 100%. **Not triggered.**
- **Lowerable share, top three to 15% (ADR 0034: evaluated on changed pairs; fewer than three
  agent pairs have any changed pairs, so this and the per-ticket rule below apply to Git
  Extensions alone, per ADR 0034's evaluation paragraph).** Git Extensions: 24.4% ≥ 15%.
  **Passes.** All three agent pairs remain pure retargets (0 changed `.cs` files each, matching
  M3-022), so the agent median is empty for this row, exactly the case ADR 0034 was written for.
- **Lowerable share, 5% bar per M4 ticket (now 5% of Git Extensions' 315 changed pairs = 16
  pairs).** Computed exactly from `changedReasonSets` (ADR 0034 item 2: a ticket's pairs unlocked
  is the number of changed pairs whose reason set is a subset of the reasons it removes, plus
  those earlier tickets in the order already remove), greedily ordered by pairs unlocked per
  effort point (S=1, M=2, L=4):

  | Order | Ticket | Reasons removed | Newly unlocked | % of 315 | Effort | Ratio |
  |---|---|---|---|---|---|---|
  | 1 | M4-001 | ConstructorBodyOperation, foreach-enumerator, using | 54 | 17.1% | L (4) | 13.5 |
  | 2 | P2-001 | ArrayCreation | 18 | 5.7% | M (2) | 9.0 |
  | 3 | M4-005 | IsType, switch-pattern | 13 | 4.1% | M (2) | 6.5 |
  | 4 | M4-004 | DelegateCreation | 25 | 7.9% | L (4) | 6.25 |
  | 5 | M4-006 | async | 9 | 2.9% | M (2) | 4.5 |
  | 6 | P2-008 | IsNull | 9 | 2.9% | M (2) | 4.5 |
  | 7 | M4-003 | ref-argument, lock | 8 | 2.5% | M (2) | 4.0 |
  | 8 | M4-002 | Binary, CompoundAssignment, Decrement | 19 | 6.0% | L (4) | 4.75 |
  | 9 | P2-002 | TypeOf | 4 | 1.3% | S (1) | 4.0 |
  | 10 | P2-003 | DefaultValue | 4 | 1.3% | S (1) | 4.0 |
  | 11 | P2-005 | EventAssignment | 4 | 1.3% | M (2) | 2.0 |
  | 12 | P2-007 | FieldReference | 2 | 0.6% | S (1) | 2.0 |
  | 13 | M4-008 | no-body, Block, catch-filter | 3 | 1.0% | M (2) | 1.5 |
  | 14 | P2-006 | FlowCaptureReference | 2 | 0.6% | M (2) | 1.0 |
  | 15 | P2-004 | EventReference | 0 | 0% | M (2) | 0 |

  **M4 tickets below the 5% bar move to the post-MVP backlog:** M4-003 (2.5%), M4-005 (4.1%),
  M4-006 (2.9%), M4-008 (1.0%). **M4 tickets kept, reordered:** M4-001, M4-004, M4-002.
  **P2 ticket clearing the bar, scheduled into M4:** P2-001 (5.7%). Every other P2 ticket
  (P2-002 through P2-009, minus P2-001) stays in the unscheduled backlog.
  `docs/ROADMAP.md`'s M4 and P2 sections are updated to this order in the same PR as this verdict.
- **Project load rate below 100% on a human pair is a ticket.** Git Extensions: 100% / 100%.
  **Not triggered.** P2-010 and P2-011 (the `IrLowerer.Destination` crash and its containment)
  are the reason this row is measurable at all; M3-031 criterion 7 (no `IrLowerer.Destination`
  result in the SARIF) also holds — 0 tool execution notifications, 0 unverified procedures.
- **Line-scoped Unknown share, seeded recall.** Not measured in `census` mode (M4-007).

## Outcome

**continue.** Git Extensions' lowerable share (24.4%) clears ADR 0028's 15% bar on its own, which
is what ADR 0034 anticipated when it made Git Extensions decide this rule alone once every agent
pair turned out to be a pure retarget. No further ADR is needed. `docs/ROADMAP.md`'s M4 list is
reordered per the table above; M4-007's `Depends on:` line drops the four backlogged tickets and
gains P2-001.

## Findings carried to tickets
- `corpus.ps1 -Packages` cannot run on any of the four pairs (a `Get-ChildItem -Recurse -Include`
  bug on this PowerShell version): P2-015. `packageVersionChanges` (ADR 0034 item 4) is still
  uncomputed for every pair; this verdict does not depend on it.
- `pmb-tomasjohansson__adapters-shortest-paths-dotnet`'s modern side loads 0 procedures where
  M3-022 loaded most of 312 matched pairs on the same commit: P2-016. It does not change this
  verdict (the pair has zero changed pairs either way and is excluded from every median here).
