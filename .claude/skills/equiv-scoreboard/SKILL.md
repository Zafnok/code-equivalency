---
name: equiv-scoreboard
description: Keep the README's scoreboard (proved Equivalent, congruent share, Unknown, Divergent, Divergent precision, soundness, large pairs that complete, run time, repeatability) equal to the latest measurements on main, and leave the README alone when no number moved. Use after any corpus run, audit or measurement that adds or changes a file under docs/runs/, when asked to update the README's numbers, coverage, speed or "where it stands", or when asked whether the README is current.
---

# README scoreboard

The scoreboard is the table in `README.md` between `<!-- scoreboard:begin -->` and
`<!-- scoreboard:end -->`, under "Where it stands". It answers "how good is `equiv` today" in one
screen. This skill says where each number comes from and when the table may be edited.

## The one rule

**Edit a row only when its value changed. If no value changed, do not touch `README.md` at all.**

Most pull requests change code and measure nothing. If each of them touched the README, every open
branch would conflict with every other on the same lines. So:

1. Only a pull request that adds or changes a measurement under `docs/runs/` may edit the
   scoreboard. A fix that "should" move a number does not: the number moves when a run measures it.
   An engine ticket's pull request never edits the scoreboard, even to say a cause is fixed.
2. Recompute every row from the sources below. Compare each value to the row as it is on `main`,
   at the row's own precision (one decimal for a percentage, minutes for a time, exact for a count).
3. Rewrite only the rows that differ, and set those rows' Measured cell. A row whose value is the
   same keeps its old Measured cell, even though a newer run confirms it.
4. There is no "as of" date for the whole table and none is to be added: a line every update
   rewrites is a line every update conflicts on.
5. If no row differs, the README is not in the diff. Say "scoreboard unchanged" in the pull request
   body and stop.
6. The prose and the detail tables below the scoreboard follow the same test: change a sentence
   only when a number in it changed.

Before pushing, rebase on `main` and read the scoreboard again. If another pull request moved a row
in between, recompute against the new `main`; never resolve a conflict by keeping your side.

## What counts as a source

A committed file on `main` or in the pull request: a `docs/runs/**/SUMMARY.md`, a
`docs/runs/*-verdict.md`, an audit or measurement report in `docs/runs/`, or the Notes of a ticket in
`docs/tickets/done/` that records a run's exit code, time and counts. Nothing from memory, from a
SARIF under `.corpus/` that no committed file summarises, or from a run on an uncommitted tree.
A row rests only on runs in `compare`'s default mode (ADR 0049); a `quick` run is no source.
A run that exits 4 with skipped projects is void (`equiv-corpus-run` section 5) and is no source.

## The rows

"The large pairs" are the human pairs in `tools/corpus/pairs.csv` with more than 50,000 analysed
lines per side that have a `full` run summarised on `main`. Each pair counts once, by its latest
run. The first cell of the table names how many pairs the aggregate covers, so a new pair entering
the set is itself a change. Counts are summed over the large pairs, never averaged from their
percentages. A changed pair is ADR 0034's: matched and not congruent.

| Row | Value | Source |
|---|---|---|
| Proved Equivalent | (congruent + solver Equivalent) / matched pairs | each large pair's latest SUMMARY |
| Equivalent by congruence | congruent / matched pairs | same |
| Changed pairs the solver proves | solver Equivalent / changed pairs | same |
| Unknown | Unknown / matched pairs, then / changed pairs, then the count by reason | same; leave `unmatched-overload` out, it is not a matched pair |
| Divergent | (EQ002 + EQ006) / matched pairs, then / changed pairs | same |
| Divergent precision | confirmed / (confirmed + false positive), undetermined left out, per pair kind (migration, upgrade, cleanup) | the newest adjudication of each kind: `*-divergent-audit.md`, `*-upgrade-verdict.md`, `*-cleanup-verdict.md` |
| Left for a reviewer | (Unknown + Divergent) / matched pairs | the SUMMARYs |
| Soundness | seeded behaviour changes reported Equivalent, over seeds applied; false Equivalents found on a real pair (a count, with the ticket each became); open `P2-nnn-soundness-*` tickets by id | newest verdict with a `seeded` run; the adjudications; `docs/tickets/` |
| Large pairs that run to a result | pairs whose latest `full` run wrote SARIF with no crashed pair (exit 0, 1 or 2) / large pairs in `pairs.csv` | SUMMARY or a done ticket's Notes |
| Run time | mean wall-clock of the large pairs' latest runs, and the range | SUMMARY `wall-clock` |
| Repeatability | results equal between two runs of one pair on one commit / results | newest report that runs a pair twice |

Other axes belong in the scoreboard only once a run reports them for the large pairs: line-scoped
Unknown share, lowerable share of changed pairs, mechanical-seed recall, Preserving Equivalent
share. Add a row in the pull request that first measures it, in this table and in the README.

A value never mixes commits silently. When the large pairs' latest runs are on different `equiv`
commits, the Measured cell lists each date; the detail table below names each run.

## Writing a row

- Percentage first, then the count it came from: `13.8% (310 of 2,246)`.
- Plain words and full metric names, as the rest of the README. No ticket ids except an open
  soundness ticket, which is linked.
- Measured is the date of the newest run the value rests on, linked to its verdict or SUMMARY.
- Do not add a "was" column or an arrow. History is in `git log -p README.md` and in `docs/runs/`.
- A value that got worse is written the same way as one that got better. Report a regression in the
  pull request body under its own heading, with the ticket it became.

## When it runs

- `equiv-corpus-run` section 7, after the verdict file is written.
- Any ticket whose Files list holds `docs/runs/` (an audit, a rerun, a measurement), as its last
  step before the pull request.
- On request ("is the README current?"): recompute, and report the rows that differ without
  editing unless asked.

Corpus rules still hold in the README: counts, percentages, reason names and times only; no source
text, no counterexample values (`docs/runs/README.md`).
