# P2-248 Measurement: the opaque reasons behind the five large pairs' Unknowns after ADR 0060, and the solver-budget and abstraction Unknowns left on edited bodies
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
ADR 0060 moved the 831 unedited bodies a runtime change reaches out of Unknown and Divergent into EQ007. What is left
as Unknown on the five large pairs at `b0f56ff8` (`docs/runs/2026-10-17-runtime-exposure-verdict.md`) is 658: opaque
construct 416 (134, 156, 18, 92 and 16 on `gitextensions-8522`, `gitextensions-9860`, `jellyfin-13023`,
`duplicati-3124`, `openra-17989`), solver budget 129 (22, 23, 17, 57, 10), abstraction 91 (44, 18, 6, 14, 9). The 596
unwitnessed exposures hold 150 more opaque, 178 `timeout` and 255 `abstraction`.

The last count of the opaque reasons over several pairs is P1-028's (2026-10-07, three pairs, 2,246 changed pairs),
before P2-122, P2-136 to P2-141, P2-179, P2-180 and P2-182 landed, and before ADR 0060. Each run summary lists the
top reasons by body, but the owner column is stale (it shows `Conversion` as "none open" while P2-215 is open), and no
one has counted which reasons, alone and in combination, keep the Unknowns opaque now, nor how much of `timeout` and
`abstraction` is the edited code the solver work should target rather than unedited bodies. Milestone 3 names that
last count as "the next measurement". Count both, and file the tickets the count supports.

## Spec references
ADR 0028 (the 5% bar), ADR 0034 (`changedReasonSets`, exact pairs unlocked), ADR 0024 (equal texts), ADR 0060,
`docs/tickets/IOPERATION-COVERAGE.md`, `docs/tickets/done/P1-028-opaque-tail-without-an-owner.md` and its result
`docs/runs/2026-10-07-opaque-tail.md` (the tables to follow), `docs/tickets/done/P2-122-switch-pattern-opaque-owner.md`
(the shape of a filed ticket), the five `docs/runs/2026-10-17-full-<slug>/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The data is the five pairs' SARIF at one commit after ADR 0060: P2-238's five runs at `b0f56ff8` when all five are
   on this machine, else a fresh `full` run of each (compare mode quick, `--jobs 4`, each pair's committed config, one
   Release build of one commit of `main`) through `equiv-corpus-run`. A fresh run never overlaps another corpus run on
   the machine, P2-247's included, and none is stopped once started. The report names the commit and each SARIF file.
2. `docs/runs/<date>-opaque-after-runtime-exposure.md` lists every opaque reason with, per pair and summed: bodies per
   side, changed pairs it is in, changed pairs it alone keeps opaque (and that share of the pair's changed pairs),
   EQ003 opaque Unknowns it is in and alone in, unwitnessed EQ007 it is in, and its open owning ticket or "none".
   Owners are read from the open tickets in `docs/tickets/` at the run's commit, at least P2-214 (`no-body`), P2-215
   (`Conversion`), P2-183 (`ArrayElementReference`), and the one-form owners (P2-178 `LocalFunction`, P2-102
   `InterpolatedString`, P2-175, P2-128); a reason a ticket owns one form of says so.
3. The reason sets: the 15 largest reason sets of the 416 EQ003 opaque Unknowns over the five pairs, each with its count
   and the owners of its reasons.
4. A marginal-unlock table: assuming every open owner lands in full, the changed pairs each unowned reason would then
   alone keep opaque, per pair and summed over the five, as a share of their 2,357 changed pairs, in greedy order (the
   reason that unlocks most first, then the next given the first), and the lowerable share today, with every open owner
   landed, and with every listed reason landed.
5. Every reason at or above 5% of one pair's changed pairs alone, or whose marginal unlock is at or above 5% over all
   five, without an open owner of that whole reason, gets a new ticket in P2-122's shape ("count the forms, lower the
   largest"), its Goal quoting its row. A reason under both lines is listed as not filed with its counts. A reason
   opaque by decision (`DynamicInvocation`, `TypeParameterObjectCreation`) is listed and never filed.
6. The solver Unknowns: the 129 `timeout` and 91 `abstraction` EQ003 Unknowns are split, per pair, by whether the
   two bodies' texts differ (an edited body) and, for the unedited ones, by `notCongruent.component`. For the edited
   ones, `timeout` by the rung and solver that ran out (`properties.rungs` or the budget pass, whichever the SARIF
   records) and `abstraction` by the abstracted function (the top 15, as each summary's "Top abstractions" line counts
   them). The 178 `timeout` and 255 `abstraction` unwitnessed EQ007 are split the same way by first rule. The report's
   last section says how many of each are on edited bodies, the measured target of the next solver work.
7. `docs/ROADMAP.md` lists the new tickets in milestone 3's Unknown item, in order of marginal unlock, and the
   section-6 counts in one line beside "`timeout` ... is the solver budget".

## Files
`docs/runs/<date>-opaque-after-runtime-exposure.md` (and `docs/runs/<date>-full-<slug>/SUMMARY.md` for a fresh run),
new `docs/tickets/P2-nnn-*.md`, `docs/ROADMAP.md`. A throwaway script under the scratch folder or `tools/spikes/`.

## Tests
None. This is a measurement ticket.

## Size guard
An edit under `src/` or `tests/`, a sixth pair, or more than six new tickets: stop and record why.

## Out of scope
Lowering anything. Fixing the summaries' stale owner column (record it; the summary script is `tools/`). Adjudicating
any result (P2-247). The `README.md` scoreboard: a measurement with no new verdict moves no row.

## Notes
- Filed 2026-10-18, beside P2-247: it needs no execution and no reach ticket, so it can run while P2-240 to P2-246 are
  implemented.
- Decision: the 5% bar is applied both per pair alone (ADR 0028's rule) and as a marginal unlock over all five ->
  P1-028 showed most of the tail is under 5% on each pair but unlocks once its co-reasons land; the user asked for both.
