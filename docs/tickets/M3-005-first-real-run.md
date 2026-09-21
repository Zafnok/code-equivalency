# M3-005 First real run
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-004, M3-008, M3-010, M3-011, M3-017, M3-018, M3-019, M3-020, M3-021, M3-022
(without them the run is mostly `Unknown(opaque)` and says little; M3-022's census may drop
precision tickets that its histogram shows are not worth their effort, and edits this line)

## Goal
Run the tool on a real 4.8-to-10 solution pair the user supplies and turn every
surprise into a ticket. Fix nothing.

## Acceptance criteria (all must hold; nothing beyond them)
1. Ask the user for the two solution paths at the start; do not proceed without them.
2. Run `equiv compare` once with defaults and once with `--fail-on unknown`; save both
   SARIF files under `docs/runs/<yyyy-mm-dd>-<slug>/` (git-ignored if the user says the
   code is private; ask).
3. Write `docs/runs/<...>/SUMMARY.md` with: procedure counts (matched, added, removed),
   verdict counts by kind and by `proofMethod`, the ten most common `IrOpaque` reasons
   with counts, the ten most common `Unknown` details, wall-clock time, and the three
   Divergent results the user finds most and least believable (ask them).
4. For every distinct `IrOpaque` reason and every crash, one post-MVP ticket file
   `P2-nnn-<slug>.md` with a minimal repro sample sketched in the Goal (not built).
5. No changes under `src/` or `tests/`.

## Size guard
If you are editing engine code, stop; that is a new ticket.

## Out of scope
Fixes. Performance work. New samples beyond the sketches in the tickets.

## Notes
