# P1-018 Measure the IL fallback's decided-verdict gain on the corpus, and turn it on by default only if it clears 5%
Status: todo
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
