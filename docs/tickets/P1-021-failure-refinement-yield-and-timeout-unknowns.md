# P1-021 Measurement: what failure refinement answers today, and what it would answer on `timeout` Unknowns
Status: in-progress
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
ADR 0037, ADR 0028 (the 5% bar), VERIFICATION-MODEL.md section 6 (`failureRefinement`, the two
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
4. If at least 5% of the `timeout` Unknowns get `none-proved` for `newFailures`, the ticket files
   `P1-nnn` to ask the queries there, starting with a clarification of ADR 0037. Otherwise it adds one
   measured line to ROADMAP's post-MVP list.
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
- Result: `docs/runs/2026-10-04-failure-refinement.md`. Today 11 of 1,072 queried Unknowns (1.0%)
  have a `none-proved` `newFailures`. Of gitextensions-8522's 164 `timeout` Unknowns, 138 are still
  a `timeout` at `a74f2e0`, and 2 of them (1.4%) would have one; 17 (12.3%) get an answer from
  either query, at 0.44 times the pair's time.
- Deviation: criterion 4. The share is 1.4%, under 5%, so the criterion asks for a ROADMAP line and
  no ticket. P1-032 is filed as well, on the user's direction of 2026-10-04 ("our goal is
  soundness/correctness across as much code as possible then speed"): measured yield orders work
  and does not by itself leave a sound gain unbuilt. The measured line is on P1-021's own ROADMAP
  entry, not in the post-MVP list, since the work is scheduled.
- Decision: the gitextensions-8522 run is P2-076's "after, again" run (164 `timeout` Unknowns). It is
  the one that makes the ticket's 400 with the other two runs' 98 and 138; the newer `--il-fallback`
  run has 155 and is not a default-config run.
- Decision: the share criterion 4 tests is over the pairs still a `timeout` at this commit. Over all
  164 it is 10.4%, but 15 of those 17 pairs are now proved Equivalent by `main`, so the weaker claim
  adds nothing to them.
- Decision: each pair is verified once first (the baseline), which the ticket does not ask for. It
  is what tells a pair `main` now decides from one that is still a `timeout`, and what the added
  time is compared with.
- Decision: the spike runs the production `FailureRefinementQuery` unchanged and reads each check's
  time and answer from the debug run log, so no query logic is copied. A query that is `unknown`
  because a check gave up is recorded as `timeout`.
- The three runs' SARIF is not kept in one place. It was read from the `.corpus/` of the worktrees
  that made the runs. The pair itself was fetched and restored again through `corpus.ps1`.
- Two measurement runs differ on 4 of 164 pairs, each on a query the resource limit ends in one run
  and not the other (P2-100).
- The Lahiri citation was not checked beyond ADR 0037, which gives it in full (Lahiri, McMillan,
  Sharma and Hawblitzel, FSE 2013). Nothing here relies on it.
