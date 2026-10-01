# P1-018 Measure the IL fallback's decided-verdict gain on the corpus, and turn it on by default only if it clears 5%
Status: in-progress
Effort: S
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-016, P1-017

## Goal
ADR 0039 ships the fallback off because the spike measured lowerable pairs, not verdicts. This
ticket runs Git Extensions (`gitextensions-8522`) in the skill's `full` mode twice, without and with
`--il-fallback`, at the same commit. It counts the changed pairs that move from Unknown(opaque) to a
decided verdict, which is Equivalent or Divergent. If they are at least 5% of changed pairs, the
default becomes on. Otherwise the default stays off and ROADMAP records the numbers.

## Spec references
ADR 0039; ADR 0028 (the 5% bar, the corpus and its criteria); ADR 0034 (changed pairs); ADR 0035
(replay confirms a Divergent); `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `docs/runs/<date>-il-fallback-verdicts.md` reports, for both runs: changed pairs; pairs lowered
   from IL; for those, the verdict before and after (Equivalent, Divergent, and Unknown by reason);
   crashes (exit 5 pairs); and wall-clock time.
2. Every Divergent that only the IL run produces is replayed with `--execute`. The report lists each
   one's `properties.replay`. A `not-reproduced` one is a P2 ticket, and it does not count towards
   the gain.
3. No pair that is Equivalent without the fallback is Divergent or crashes with it. If one does, the
   default stays off and a P2 ticket is filed, whatever the gain.
4. If the gain (criterion 1's moves to Equivalent, plus reproduced Divergents) is at least 5% of
   changed pairs and criterion 3 holds: `--il-fallback` defaults to on, a `--no-il-fallback` option
   exists, `properties.lowering` is always written, and ARCHITECTURE.md and ADR 0039 get a dated
   `## Clarifications` bullet with the numbers. Otherwise: the default stays off and ROADMAP's
   post-MVP list gets one line with the numbers.
5. The report contains counts, kinds, reasons and identities only, no source text
   (`docs/runs/README.md`).

## Files
- `docs/runs/<date>-il-fallback-verdicts.md`
- If on by default: `src/Equiv.Cli/CompareCommand.cs`, its tests and snapshots, `docs/ARCHITECTURE.md`,
  `docs/adr/0039-il-fallback-lowering.md` (Clarifications only)
- Otherwise: `docs/ROADMAP.md`

## Tests
If on by default: `CompareCommandTests.IlFallbackIsOnByDefault`, `.NoIlFallbackTurnsItOff`. None otherwise.

## Size guard
Any change to the lowering itself is a finding. File it as a ticket, not a fix here.

## Out of scope
Catalogue entries for `Nullable<T>` getters or `string.Format` (a finding, and a ticket if the report
shows they block the gain). Lambdas. The other corpus pairs: they are pure retargets, which congruence
already decides.

## Notes
- Decision: three runs at one commit, not two. The two measured runs are `full` without and with
  `--il-fallback`, and neither takes `--execute`: replay turns some Unknowns into observed Divergents
  (M4-009), which would mix into the move being measured. A third run, `--il-fallback --execute`,
  supplies criterion 2's `properties.replay` for the Divergents only the IL run produces.
- Decision: the three runs ran concurrently on one box, with starts staggered until the previous run
  had finished loading (the 2026-09-30 full run lost a pair of runs to MSBuild lock collisions). So
  wall-clock times are under shared load, but equally so for both measured runs.
- Decision: the gain's denominator is ADR 0034's changed pairs from the census (`matchedPairs -
  pairsCongruent`). The 19 `unmatched-overload` results are not matched pairs. The numerator counts
  IL-lowered pairs that move out of Unknown(opaque), which is ADR 0039's wording. Moves out of any
  other Unknown reason are reported next to it but do not count.
- Result: 21 of 1,294 changed pairs (1.6%) move from Unknown(opaque) to Equivalent. 21 more move to
  Divergent, and replay reproduces none of them (27 `not-constructible`, 1 `not-applicable` over the
  28 Divergents only the IL run has), so none counts. The gain is under 5%: the default stays off
  and ROADMAP's Post-MVP list has the line. Criterion 3 holds.
- Changed pairs are 1,294 at 1d4569a, not the 1,143 of the 2026-09-30 run at bd8e379. The 5% bar is
  therefore 65 pairs.
- The `--il-fallback` runs exit 5: one IL-lowered pair, Unknown(timeout) without the fallback, makes
  the encoder throw a Z3 sort mismatch. Filed as P2-078. It is outside criterion 3's wording (the
  pair was not Equivalent), and the default stays off anyway.
- One IL-lowered pair goes from Divergent to Unknown(timeout). Recorded in P2-078's Notes.
- Seven pairs the fallback never lowered changed verdict between the two measured runs, all to or
  from a timeout: wall-clock budgets under shared load (P2-050). They are reported apart from the gain.
- The corpus checkout was reused from the `corpus-runs-debug-progress` worktree's `.corpus/` through a
  directory junction, so nothing was fetched or restored again. Run directories:
  `.corpus/pairs/gitextensions-8522/runs/20260930-p1018-{full,il,il-execute}`.
- Each run took about 8h15m. Six pairs that end Unknown take most of the verify phase, and about two
  hours follow in a pass no phase logs. Filed as P2-076 and P2-077 in this PR, at the user's request
  to speed up migration runs without ending any pair early.
- Decision: the report is dated 2026-10-01, the day the runs finished and it was written. The runs
  started 2026-09-30, and their directories carry that date.
