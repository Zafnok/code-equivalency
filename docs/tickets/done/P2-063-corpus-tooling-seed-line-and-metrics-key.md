# P2-063 Corpus tooling: `seeds.json` records the mutated line, `-Metrics` reads `unknownReason`
Status: done (PR #318)
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-046

## Goal
Three small tooling gaps that P2-046 worked around by hand:
1. The mechanical seeder writes each seed's `line` as the method's first line, not the line it
   changed (a seed at `Equals(object)` reports line 108 and mutates line 121). The skill's mechanical
   recall counts an Unknown "with a `relatedLocation` on the seeded line", which cannot be checked, so
   recall was not computed.
2. `corpus.ps1 -Metrics` groups Unknown by a property named `reason`, but the SARIF calls it
   `unknownReason`, so it falls back to the first word of the message.
3. Two `equiv compare` runs started at the same time on the same checkout collide on MSBuild files
   (`obj/**/*.AssemblyReference.cache`, "being used by another process"): projects are skipped and
   the run exits 4. P2-046 lost a `full` and a `full --execute` run to it. The skill does not say so.

## Spec references
`tools/corpus/seeder/`, `tools/corpus/corpus.ps1` (`-Metrics`), `.claude/skills/equiv-corpus-run/SKILL.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each entry in a seeds manifest has `line` = the first line the mutation changed, with a test in
   `tests/Equiv.Corpus.Seeder.Tests` that a mutation on the fourth line of a method reports that line.
2. `-Metrics` prints Unknown by `unknownReason`, and by `scope` within it.
3. The skill's section 5 says, in one sentence, that runs on the same pair must not overlap their
   load phase (start the second only after the first's `load-modern` line), and that a run that
   exits 4 with skipped projects is void.

## Files
`tools/corpus/seeder/` and its tests, `tools/corpus/corpus.ps1`, `.claude/skills/equiv-corpus-run/SKILL.md`.

## Tests
`SeedManifestTests.LineIsTheMutatedLine`.

## Size guard
If the seeder cannot tell the mutated line (an operator that rewrites several), record the first.

## Out of scope
The seed operators themselves, recall thresholds.

## Notes
- Found by P2-046.
- Deviation: there is no `tests/Equiv.Corpus.Seeder.Tests` directory. The project of that name lives at
  `tools/corpus/seeder.Tests/`, so `SeedManifestTests` is there.
- Decision: `line` is the line of the first character at which the mutated file's text departs from the
  original's (`MethodSeeder.FirstChangedLine`), not a position each operator reports. It needs no change to
  the operators (out of scope), and every line before it is identical, so the number is the same line in
  the unseeded and the seeded file. When a removed statement is followed by an identical one the first
  difference is later than the removed line; that is the size guard's "record the first".
- Decision: an EQ003 result with no `unknownReason` (a SARIF older than the property) prints as `n/a`, like
  every other absent property in `-Metrics`; the first-word-of-the-message fallback is gone.
- `-Metrics` was checked by hand on a six-result SARIF (`corpus.ps1` has no test harness): it printed
  `abstraction 3` with `whole 2`, `partial 1` under it, then `n/a 1` and `timeout 1`.
- `powershell -File tools/corpus/corpus.ps1` from Git Bash fails on this box's execution policy; run it
  from a PowerShell session instead.
