# P1-028 Measurement: the opaque reasons no ticket owns, counted over the three large runs together
Status: todo
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
