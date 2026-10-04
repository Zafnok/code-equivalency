# P1-021 Measurement: what failure refinement answers today, and what it would answer on `timeout` Unknowns
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-013, P2-050

## Goal
Differential assertion checking (Lahiri et al., FSE 2013; citation from memory, check before relying
on it) asks a weaker question than equivalence: can the new version fail where the old one did not?
ADR 0037 is that question, and P1-013 built it. Two things about it have never been measured:
- how often it answers `none-proved` or `found`, and how often `unknown`, on real Unknowns;
- what it would answer on a `timeout` Unknown. ADR 0037 does not ask there, on the argument that "the
  weaker query seldom finishes where the full one did not". That is a guess. `timeout` is the second
  largest Unknown reason on the three large runs (400 of 1,471), and all of them are `method` scoped,
  so they claim nothing at all today.

Measure both. Answer with counts, not a design.

## Spec references
ADR 0037, ADR 0049 (decision 7: yield chooses the mode), VERIFICATION-MODEL.md section 6 (`failureRefinement`, the two
budgets), `docs/runs/2026-10-01-timeout-budget.md`, the P1-019 spike (the shape to follow).

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/spikes/failure-refinement/` reads the SARIF of the three large runs (`gitextensions-8522`,
   `gitextensions-9860`, `jellyfin-13023`) and tabulates `failureRefinement.newFailures` and
   `removedFailures` by outcome and by `unknownReason`.
2. For every `timeout` Unknown of `gitextensions-8522`, the tool builds the pair as P1-019's spike
   does and asks ADR 0037's two queries at the default `resourceLimit`. It records, per query,
   `none-proved`, `found`, `unknown` or timeout, and the solver time.
3. `docs/runs/<date>-failure-refinement.md` holds both tables, the added solver time as a share of the
   run's verify time, and one line each for: the share of today's queried Unknowns with a
   `none-proved` `newFailures`, and the share of the `timeout` Unknowns that would have one.
   Identities and counts only.
4. The report ends with one line on whether quick mode should ask the queries on `timeout` Unknowns
   too, from the measured time and yield, and the ticket adds that line to ROADMAP beside P1-032.
   Thorough mode asks them whatever the number is (ADR 0049, P1-032), so no follow-up ticket is
   filed for that.
5. Nothing under `src/` changes. The spike is not in `Equiv.slnx`.

## Files
`tools/spikes/failure-refinement/**`, `docs/runs/<date>-failure-refinement.md`, `docs/ROADMAP.md`,
and the follow-up ticket if criterion 4 calls for it.

## Tests
A `--self-test` over two hand-written pairs: one whose weaker query is unsatisfiable, one with a
model.

## Size guard
Any edit under `src/`, or a rerun of a full corpus run: stop. The corpus is used only through
`.claude/skills/equiv-corpus-run`.

## Out of scope
User assertions (`Debug.Assert`, contract calls) as checked properties. Changing ADR 0037's rule for
`unbound`.

## Notes
- From the 2026-10-03 improvement review (the "Differential assertion checking" row). The review lists
  it as absent; it is ADR 0037 and P1-013, and what is absent is the measurement.
- 2026-10-04: criterion 4 rewritten under ADR 0049. It gated the queries on a 5% share; the
  measurement now only decides whether quick mode asks them as well.
