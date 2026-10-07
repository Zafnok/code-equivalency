# P1-028 Measurement: the opaque reasons no ticket owns, counted over the three large runs together
Status: in-progress
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
An opaque construct is the largest Unknown reason on the three large runs: 733 of 1,471. ADR 0028
gives a reason an owner when it alone keeps at least 5% of one pair's changed pairs opaque. The large
reasons have owners (P2-123 `Conversion`, P2-122 `switch-pattern`, P2-087 `Binary`, P2-120
collection expressions, P2-096 LINQ). What is left is a tail that the rule cannot see: each reason is
under 5% on each run, and most changed pairs in the tail hold two or three reasons at once, so no
single ticket unlocks them. On `gitextensions-9860` the report marks `InstanceReference`,
`DefaultValue`, `CaughtException`, `DeconstructionAssignment`, `iterator`, `ArrayElementReference`,
`CompoundAssignment`, `Tuple`, `ref-argument` and `ArrayCreation` as "none", and reason sets such as
`Conversion+DefaultValue` (19 pairs) and `Conversion+switch-pattern` (14) as "none" too.

Count the tail once, over all three runs, by what lowering each reason would unlock in combination
with the open owners, and file the tickets the count supports.

## Spec references
ADR 0028 (the bar), ADR 0034 (`changedReasonSets`, exact pairs unlocked), ADR 0027,
`docs/tickets/IOPERATION-COVERAGE.md`, the three `docs/runs/2026-10-03-full-*/SUMMARY.md` and the
current `gitextensions-8522` run, `docs/runs/2026-09-30-full-verdict.md` (the per-ticket unlock table
to follow).

## Acceptance criteria (all must hold; nothing beyond them)
1. From the three runs' `loweringCensus.changedReasonSets` (their SARIF under `.corpus/`, no new
   run), `docs/runs/<date>-opaque-tail.md` lists every opaque reason with: bodies per side, changed
   pairs it is in, changed pairs it alone keeps opaque, and its open owning ticket or "none", per run
   and summed.
2. A second table assumes every open owner lands in full, and gives for each unowned reason the
   changed pairs it would then alone keep opaque (its marginal unlock), summed over the three runs
   and as a share of their 2,246 changed pairs.
3. A third table gives the lowerable share of each run today, with every open owner landed, and with
   the whole tail landed. That is the ceiling IR coverage can reach on these runs.
4. Every unowned reason whose marginal unlock is at least 1% of the 2,246 changed pairs gets a
   ticket, in the shape of P2-122: count the forms, lower the largest. Each new ticket's Goal quotes
   its row. At most six tickets; the rest are listed in the report as not filed, with their counts.
5. `docs/ROADMAP.md` lists the new tickets in order of marginal unlock.

## Files
`docs/runs/<date>-opaque-tail.md`, new `docs/tickets/P2-nnn-*.md`, `docs/ROADMAP.md`. A throwaway
script under the scratch folder or `tools/spikes/`.

## Tests
None. This is a measurement ticket.

## Size guard
Any edit under `src/`, or a new corpus run: stop.

## Out of scope
Lowering anything. Reasons that are opaque by decision (`DynamicInvocation`, P2-029). The base class
library members behind `abstraction` Unknowns (floating point, `decimal`, `string` operators): they
are not opaque nodes, and P1-019 measured them.

## Notes
- From the 2026-10-03 improvement review (its first priority, "Expand IR coverage").
- Result: `docs/runs/2026-10-07-opaque-tail.md`. The open owners take the lowerable share of the
  2,246 changed pairs from 34.1% to 55.6%; the unowned reasons hold the other 44.3%; the ceiling is
  99.9%. Filed P2-136 (`DelegateCreation`, 257 pairs, 11.4%), P2-137 (`rebound-call`, 191, 8.5%),
  P2-138 (`DefaultValue`, 73), P2-139 (`InterpolatedString`, 59), P2-140 (`CaughtException`, 51) and
  P2-141 (`CompoundAssignment`, 35).
- The three SARIF files are `gitextensions-8522` `20261002-1814-full-after` (P2-076's run, 951
  changed pairs), and `gitextensions-9860` and `jellyfin-13023` `20261003-0055-full` (722 and 573).
  They are the only three whose changed pairs sum to 2,246 and whose `opaque` Unknowns sum to 733.
  This worktree has no `.corpus/`; they were read from the worktrees that made them.
- Decision: which reasons count as owned -> a reason a ticket lowers as a whole, open today or
  merged after the runs' commit (`Conversion`, `switch-pattern`, `Binary`, `await-using`,
  `CollectionExpression`, `no-body`). A reason whose owner was done before the runs and is still in
  them (`DelegateCreation`, `InterpolatedString`), or of which an open ticket owns one form
  (`DefaultValue`: P2-095; `InterpolatedString`: P2-102), counts as unowned, with that ticket named
  in its row. The other reading would assume those reasons gone and file nothing for them, and the
  Goal itself lists `DefaultValue` and `Conversion+DefaultValue` as unowned.
- Decision: `rebound-call` counts as unowned, not as opaque by decision -> ADR 0042 keeps a rebound
  call opaque only until a catalogue entry names its two callees, P2-070 is done, and the reason is
  alone in 149 pairs (11.9% of `gitextensions-8522`'s). The Goal does not name it. Only
  `DynamicInvocation` and `TypeParameterObjectCreation` are treated as opaque by decision.
- Decision: the report adds a greedy order, the pairs of reasons that unlock most together, and a
  check against the newest census of each pair on this machine (1,743 changed pairs) -> the ticket
  asks what each reason unlocks "in combination", and the three runs predate P2-099, P2-113, P1-029
  and P2-127. The check keeps the six in the same order above the line.
- Surprise: the tail is not mostly pairs with several unowned reasons. Of the 997 changed pairs that
  hold one, 781 need a single unowned reason once the owners land.
- Not filed, over the 1% line: `iterator`, 27 pairs (1.2%), the seventh; criterion 4 caps the
  tickets at six. Not in the three runs, so outside criterion 4: `LocalFunction`, which P2-127 added
  and which unlocks 48 pairs on `gitextensions-8522`'s 2026-10-06 run. Neither has an owner.
- The throwaway script is Python (`tools/spikes/opaque-tail/`), not a project: it reads three JSON
  files and needs no build.
