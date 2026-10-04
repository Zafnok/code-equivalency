# P2-130 Scoreboard rerun: the three migration pairs on one commit, and the README scoreboard from every large pair
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-073, P2-074, P2-075, P2-103, P2-117, P2-118, P2-122, P2-087, P2-095, P2-093, P2-104, P1-028, P2-127, P2-077, P1-032, P2-124, P2-110

## Goal
The README's scoreboard rests on runs of 2026-10-02 and 2026-10-03. Its two weakest rows are
Divergent precision (3.8% on migrations and upgrades, 0% on cleanups) and the Unknown share of
changed pairs (65.5%). Fixes for the largest causes have merged since and no run has measured them:
runtime-change rows with no change point (120 of the upgrades' 126 false positives, and 353 unedited
Jellyfin bodies out of congruence), build-location constants (45 of the cleanups' 52 Divergent),
forwarders, rebound calls and overloads, effect-free calls (26 of the migration's 50 false
positives), the lowering crashes, and `await using`. Duplicati and OpenRA now run to a result and
have no summary on `main`.

The `Depends on` line is the milestone. It holds every open ticket that owns a known false-positive
cause (P2-073, P2-074, P2-075 are the migration audit's other 24; P2-103 is all 5 of PowerShell's;
P2-117 and P2-118 are the upgrades' other 6), the opaque reasons with a measured share at or above
5% of a pair's changed pairs or a cleanup sample behind them (P2-122, P2-087, P2-095, P2-093,
P2-104), the tail count that says what coverage can still reach (P1-028), the one known
false-Equivalent path (P2-127), and parallel verification (P2-077), which is the run-time row and
makes this ticket's eight hours of runs shorter. P1-032 is on it because ADR 0049 makes thorough
the default mode, and the scoreboard states what a default run gives. P2-124 (the two upgrades)
and P2-110 (the three cleanups) carry the same milestone and run first.

Run the three migration pairs, adjudicate, and bring the scoreboard to the latest run of every large
pair.

## Spec references
`.claude/skills/equiv-corpus-run`, `.claude/skills/equiv-scoreboard`, ADR 0028, ADR 0034,
`docs/runs/2026-09-30-full-verdict.md`, `docs/runs/2026-09-30-divergent-audit.md`,
`docs/runs/2026-10-01-migrations-verdict.md`, and P2-124's and P2-110's verdict files.

## Acceptance criteria (all must hold; nothing beyond them)
1. `gitextensions-8522`, `duplicati-3124` and `openra-17989` are each run in `full` mode, in
   `compare`'s default mode (the summary records `run.properties.mode`), through `equiv-corpus-run`, one after the other, at one commit of `main`: the commit P2-124 and P2-110
   ran at when nothing under `src/` has merged since, otherwise `main`'s head, with the verdict
   naming both commits. Each has a `docs/runs/<date>-full-<slug>/SUMMARY.md`.
2. No run has a lowering notification, an entry in `run.properties.unverified` or exit 5. If one
   does, record the procedure identity, file or reopen the crash ticket, and write the verdict from
   the pairs that completed, saying which did not.
3. `docs/runs/<date>-migrations-verdict.md` has, per pair and next to the last summarised value:
   matched pairs, congruent pairs, changed pairs, proved Equivalent by `proofMethod`, Unknown by
   reason, EQ002 and EQ006. It names which of this ticket's `Depends on` tickets, and of P2-068,
   P2-069, P2-070, P2-071, P2-098, P2-105 and P2-113, were done at the commit that ran.
4. Every EQ002 and EQ006 on `gitextensions-8522` in P2-047's sample is adjudicated again by P2-047's
   method; a result whose procedure and cause are unchanged keeps its classification without a new
   trace. A sample of the same size is drawn and adjudicated on each of Duplicati and OpenRA, or
   every Divergent where a pair has fewer. The verdict gives Divergent precision per pair, and for
   each false positive the open ticket that owns its cause or a new `P2-nnn` ticket.
5. The verdict has one table that sets the two gaps against the milestone: for each ticket on the
   `Depends on` line, the false positives or changed pairs its Goal claimed, and what the runs show.
6. Each crash, and each false-positive cause or opaque reason at or above 5% of a pair's changed
   pairs with no open ticket, is filed as a ticket.
7. `equiv-scoreboard` is applied over the large pairs with a summary on `main` (five, once this
   ticket's runs are in): each row whose value changed is rewritten, and no other line of
   `README.md` is. The pull request body lists the rows that moved, old value and new.
8. `docs/ROADMAP.md`'s scoreboard milestone section records the date, the commit and the verdict
   file, and names the next milestone's tickets: the ones criterion 5 and 6 rank highest against
   the Unknown and Divergent precision rows.

## Files
`docs/runs/`, `README.md`, `docs/ROADMAP.md`, new ticket files (criteria 4 and 6). Nothing under
`src/`, `tests/` or `tools/`.

## Tests
None.

## Size guard
A change under `src/`, `tests/` or `tools/`, or a fourth pair, means the ticket was misread: file
what you found.

## Out of scope
`--execute`, `seeded` and `seeded-mech` runs. The upgrade and cleanup pairs (P2-124, P2-110). Fixing
anything a run finds. A README edit to a row whose value did not change.

## Notes
- Filed 2026-10-04. The milestone was chosen from the measured counts in the audits and verdicts
  named above, not from effort.
- Not on the milestone, and why: P1-033 (cvc5) decided 13 of 164 `timeout` Unknowns in P1-025's
  spike and proved no pair; P1-030 and P1-031 have a measured yield of 1.0% of Unknowns or none;
  P2-106, P2-107 and P2-128 move PowerShell and one cleanup pair only. `timeout` (400) and
  `abstraction` (246) are 44% of the scoreboard's Unknowns and no open ticket has a measured yield
  above 8% of either. P2-101 and P1-034 are the two measurements that could find one; they are the
  first candidates for criterion 8's next milestone. This orders the work and vetoes none of it
  (ADR 0049 decision 7).
- Last recorded wall-clock: `gitextensions-8522` 2h05m, `duplicati-3124` 3h26m, `openra-17989`
  1h10m, one pair at a time. Never stop a run in progress.
