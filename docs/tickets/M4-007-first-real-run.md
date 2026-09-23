# M4-007 First full corpus run: verdicts, seeded recall, and the success criteria
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-004, M3-022, M4-001, M4-004, M4-008
(without them the run is mostly `Unknown(opaque)` and says little; M3-022's census may drop
precision tickets that its histogram shows are not worth their effort, and edits this line)

## Goal
Run the full tool on the public corpus (ADR 0028), exactly as a user would after an agent migrated
their code. Evaluate every ADR 0028 criterion, and turn every surprise into a ticket. Fix nothing.
(Until 2026-09-23 this ticket named an unspecified real pair. ADR 0028 replaced it with the
corpus.)

## Acceptance criteria (all must hold; nothing beyond them)
1. Run the `equiv-corpus-run` skill in `full` mode, then in `seeded` mode, on the same pairs
   M3-022 used: the Git Extensions pair and the agent pairs.
2. Each run's `SUMMARY.md` (skill template) has:
   - procedure counts (matched, added, removed);
   - verdict counts by kind, by `proofMethod` and by Unknown `scope` (ADR 0029);
   - the ten most common Unknown reasons;
   - the repo's own test results on both sides (`verify_command` from the manifest);
   - unchanged share, lowerable share, project load rate, line-scoped Unknown share and seeded
     recall (ADR 0028);
   - wall-clock time.
3. Seeded recall is 100%: no seeded change is reported Equivalent. Any miss is written first, as
   a `P2` ticket titled `soundness:` with the seed id, and reported to the user before anything
   else.
4. Every test that passes on the legacy side and fails on the modern side is listed. Next to it
   goes the verdict of each procedure the test's stack trace names. Any such procedure reported
   Equivalent is a soundness ticket, as in criterion 3.
5. Ask the user to name the three Divergent results they find most believable and the three least
   believable. Record their identities and the reasons in the summary.
6. For every distinct remaining `IrOpaque` reason and every crash, one post-MVP ticket file
   `P2-nnn-<slug>.md`, with a minimal repro in your own code sketched in the Goal.
7. No changes under `src/` or `tests/`. No third-party source text committed.

## Size guard
If you are editing engine code, stop; that is a new ticket.

## Out of scope
Fixes. Performance work. New samples beyond the sketches in the tickets. Any code not listed in
`tools/corpus/`.

## Notes
