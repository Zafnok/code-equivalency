# P2-046 Second full corpus run: measure again after the M4-007 fixes
Status: done (PR #305)
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-031, P2-032, P2-033, P2-034, P2-035, P2-039, P2-045 (all done)

## Goal
M4-007's run (`docs/runs/2026-09-27-m4-007-verdict.md`, equiv fd400e9) is the only full measurement
of the project, and 41 commits have landed since. Among them are the crash fixes for 92 of Git
Extensions' pairs (P2-031 to P2-034), the seeder fix that left mechanical recall resting on 23 seeds
(P2-035), the `--execute` hang (P2-039), and eight opaque reasons (P2-023 to P2-030). Every rate
quoted in the 2026-09-28 success assessment is therefore stale. Rerun M4-007 exactly, on the same
four pairs, and add the three measurements the assessment needed but could not get. Fix nothing.

## Spec references
ADR 0028 (criteria), ADR 0034 (changed pairs, per-ticket unlock rule), ADR 0029 (scope),
`.claude/skills/equiv-corpus-run/SKILL.md` (the procedure and the SUMMARY template).

## Acceptance criteria (all must hold; nothing beyond them)
1. The skill runs `full`, `seeded` (hand-written seeds and `-SeedMechanical -Count 300`) and
   `full --execute` on `gitextensions-8522` and on the three agent pairs M4-007 used, reusing their
   existing modern sides in `.corpus/` (no new agent migration). One `SUMMARY.md` per pair and one
   `docs/runs/<date>-full-verdict.md`, in the skill's shape.
2. The verdict file has a "Since M4-007" table: for each ADR 0028 metric and for the counts EQ001 to
   EQ006, pair-level crashes, solver-proved Equivalent on changed pairs (`proofMethod` other than
   `congruence`), and Unknown by reason, the M4-007 value next to this run's.
3. Mechanical seeds run on Git Extensions (P2-035 fixed the crash). Its SUMMARY reports the
   Preserving family per operator: applied, Equivalent, Unknown, Divergent. The Preserving
   Equivalent share, `Equivalent / applied`, is reported next to seeded recall and labelled as
   reported only. ADR 0028 sets no threshold for it.
4. Each SUMMARY's Verdicts section gains a "Top abstractions" line. It counts the entries of
   `properties.abstractions` over every `abstraction` Unknown, grouped by kind (`IrPure` operator
   name, or `opaque:` fragment reason), top 15. It gives identities and kinds only, never
   `candidateCounterexample` values.
5. ADR 0034 item 2's per-ticket unlock table is recomputed from this run's Git Extensions
   `changedReasonSets` for every opaque reason still present. A reason whose removal would unlock at
   least 5% of changed pairs gets a P2 ticket if it has no open owner, and the table is in the
   verdict file.
6. Seeded recall is 100%, or the miss is a `P2-nnn-soundness-<slug>.md` ticket, shown to the user
   before anything else (skill section 5).
7. Every new crash, `not-reproduced` replay, and Preserving seed reported Divergent is a P2 ticket
   (M4-007 criterion 6's rule). No changes under `src/` or `tests/`.

## Files
`docs/runs/<date>-full-<slug>/SUMMARY.md` (4), `docs/runs/<date>-full-verdict.md`, new
`docs/tickets/P2-nnn-*.md`, `docs/ROADMAP.md`. `.claude/skills/equiv-corpus-run/SKILL.md` only to add
criterion 3's and 4's lines to the SUMMARY template.

## Tests
None. This is a measurement ticket.

## Size guard
If you are editing engine code, stop; that is a new ticket.

## Out of scope
New corpus pairs or a new migration of an agent pair. Adjudicating Divergent results (P2-047).
Any threshold change (a new ADR).

## Notes
- Decision: the `--fail-on unknown` run the skill asks for is skipped again. `--fail-on` only changes
  the exit code, so a second full pass adds nothing (M4-007 made the same call).
- Decision: M4-007's hand-written seed copies were not kept, so the 28 seeds were re-applied to the
  same methods, one catalogue change each, in `modern-seeded` copies under `.corpus/`.
- Decision: the four Git Extensions runs were started concurrently to save wall-clock. The first
  `full` and `full --execute` collided on MSBuild files (skipped projects, exit 4) and were rerun one
  after the other; the void pair's data is not used anywhere. See the verdict file and P2-063.
- Decision: mechanical recall is reported as counts, not a rate, because `seeds.json` holds the
  method's first line (P2-063). Changing seeds reported Equivalent were checked by source diff; all
  are commutative operand swaps.
- Observed: a `full` run on Git Extensions took 8h57m with four runs at once (2h37m alone in M4-007).
  Timeout Unknowns are not comparable across the two runs (P2-050).
- Result: `docs/runs/2026-09-30-full-verdict.md`, verdict continue; tickets P2-060 to P2-063.
