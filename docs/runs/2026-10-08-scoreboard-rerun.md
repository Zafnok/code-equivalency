# Scoreboard rerun: the three large pairs behind the README, at one commit (2026-10-08)

`gitextensions-8522` (.NET Framework 4.8 to .NET 5), `gitextensions-9860` (net5.0 to net6.0) and
`jellyfin-13023` (net8.0 to net9.0), each run in `full` mode at equiv `18b751f7`, one after the
other, on a box that ran nothing else. Per-pair detail is in
`docs/runs/2026-10-08-full-<slug>/SUMMARY.md`. Nothing under `src/` was changed.

This is a measurement, not a verdict. It applies none of ADR 0028's criteria, adjudicates no
Divergent result, and is not the rerun tickets P2-124, P2-110 and P2-130 describe: those wait for
their milestone and adjudicate every EQ002 and EQ006 again. It brings the count rows of the README's
scoreboard to what `main` gives today.

## How these runs differ from the ones they replace

| | Runs of 2026-09-30 and 2026-10-03 | These runs |
|---|---|---|
| equiv commit | `bd8e379`, `8e0ed3c` | `18b751f7` |
| Compare mode | before the modes (ADR 0049) | `quick`, the default (ADR 0052): bound 3, resourceLimit 2000000, timeoutMs 60000 |
| Pairs verified at a time | 1 | 4 (`--jobs 4`, ticket P2-077) |
| Machine | the two upgrades shared it | one run at a time, nothing else running |

So the run times are not a like-for-like speed-up, and a count can differ because of the mode as
well as because of a fix. ADR 0052 measured the mode's effect on `gitextensions-8522`: against the
default before the modes, quick leaves 11 results Unknown, all EQ006, and loses no Equivalent.

## The table

Shares are over ADR 0034's changed pairs. An Unknown that is `unmatched-overload` is not a matched
pair and is left out. "Before" is the value the README's scoreboard rested on.

| Pair | | Matched pairs | Congruent pairs | Changed pairs | Proved Equivalent by the solver | Unknown | Divergent | Wall-clock |
|---|---|---|---|---|---|---|---|---|
| gitextensions-8522 | before | 13541 | 12590 (93.0%) | 951 | 71 (7.5%) | 588 (61.8%) | 292 (30.7%) | 2h05m |
| | now | 13592 | 12682 (93.3%) | 910 | 45 (4.9%) | 596 (65.5%) | 269 (29.6%): EQ002 30, EQ006 239 | 364 s |
| gitextensions-9860 | before | 14020 | 13295 (94.8%) | 722 | 239 (33.1%) | 421 (58.3%) | 62 (8.6%) | 1h11m |
| | now | 14073 | 13473 (95.7%) | 600 | 261 (43.5%) | 324 (54.0%) | 15 (2.5%): EQ002 8, EQ006 7 | 317 s |
| jellyfin-13023 | before | 14503 | 13927 (96.0%) | 573 | 0 (0%) | 462 (80.6%) | 111 (19.4%) | 1h32m |
| | now | 14533 | 14370 (98.9%) | 163 | 43 (26.4%) | 110 (67.5%) | 10 (6.1%): EQ002 10, EQ006 0 | 181 s |
| All three | before | 42064 | 39812 (94.6%) | 2246 | 310 (13.8%) | 1471 (65.5%) | 465 (20.7%) | mean 1h36m |
| | now | 42198 | 40525 (96.0%) | 1673 | 349 (20.9%) | 1030 (61.6%) | 294 (17.6%) | mean 287 s |

Unknown by reason, matched pairs only:

| Pair | opaque | abstraction | timeout | unaligned-loop | recursion |
|---|---|---|---|---|---|
| gitextensions-8522 | 256 | 218 | 109 | 12 | 1 |
| gitextensions-9860 | 248 | 26 | 48 | 0 | 2 |
| jellyfin-13023 | 65 | 12 | 23 | 10 | 0 |
| All three, now | 569 | 256 | 180 | 22 | 3 |
| All three, before | 733 | 246 | 400 | 89 | 3 |

Every run exits 1 (Divergent results exist), with 0 unverified procedures and 0 tool execution
notifications: no pair-level crash, where each of the three earlier runs lost one to three pairs to
a lowering crash. Every project loads on both sides. No query ended on the wall clock; the resource
limit ended 179, 87 and 49.

## What moved

- **The two upgrades.** Changed pairs fall from 1295 to 763 and Divergent from 173 to 25. EQ006 falls
  from 161 to 7: the runtime rows now have change points (ticket P2-113), which was the cause of 120
  of the 126 false positives the 2026-10-03 verdict adjudicated. `jellyfin-13023` goes from no proved
  pair to 43.
- **The migration.** `gitextensions-8522` moves little: 41 fewer changed pairs, 23 fewer Divergent,
  8 more Unknown, and 26 fewer pairs proved by the solver. Which of those 26 the mode accounts for and
  which a fix moved into congruence or into Unknown was not separated here.
- **Matched pairs rise on all three** (by 51, 53 and 30). Ticket P2-145 made `extern` methods
  procedures; the rise was not attributed further.
- **Solver-budget Unknowns fall from 400 to 180** and abstraction rises from 246 to 256.

## What was not measured

- Divergent precision. No EQ002 or EQ006 was traced, so the README's precision row and its
  adjudication counts stay as they were, and they describe the earlier runs' Divergent results, not
  these 294.
- Whether a behaviour change either pull request made is still reported, pair by pair. The seeded
  runs were not repeated.
- The effect of each ticket merged since `8e0ed3c`. The table says what `main` gives, not why.
- `duplicati-3124` and `openra-17989`, which have no summary on `main` and are P2-130's.
- Repeatability at `--jobs 4`: each pair was run once.
- Tests on either side, `--execute`, and package changes.

## Scoreboard

`equiv-scoreboard` was applied. Rows rewritten: the header's pair count, Proved Equivalent,
Equivalent by congruence, Changed pairs the solver proves, Unknown, Divergent, Left for a reviewer,
Run time, and the open soundness ticket named in the Soundness row (P2-127 is done; P2-146 is open).
Rows left alone: Divergent precision, the seeded count in Soundness, Large pairs that run to a
result, Repeatability.
