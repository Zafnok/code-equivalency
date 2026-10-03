# P2-058 Cleanup pairs in the corpus: pin three public "no functional change" PRs and run them
Status: done (PR #352)
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-055, P2-047 (the adjudication method)

## Goal
No public benchmark of behaviour-preserving C# commits exists. The 2026-09-28 search found only
Java datasets (Refactoring Oracle, PureRefactor), benchmarks without C# (SWE-Bench ProMax,
CodeTaste), and synthetic pairs (EqBench, EquiBench). Real C# cleanup PRs do exist, whose authors
say they change no behaviour. Pin three of them as ADR 0040's `cleanup` kind, run `equiv` on them, and
report how much of a cleanup commit it can prove, and whether any cleanup changed behaviour.

## Spec references
ADR 0040 decision 5; ADR 0028 decisions 1-3 (pinning, nothing third-party in git);
`.claude/skills/equiv-corpus-run/SKILL.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/corpus/pairs.csv` gains three `cleanup` rows, with full 40-character SHAs. The legacy
   commit is the merge commit's first parent.
   - `gitextensions-11372`: collection expressions, squash commit `1cfb0e44…`.
   - `gitextensions-11284`: IDE0008, explicit types for `var`, squash commit `89962d9f…`.
   - `powershell-19687`: IDE0019, `as` plus a null check becomes an `is` pattern, merge commit
     `1c55e02d…`, MIT.

   A pair whose solution does not load is replaced by another PR from the same repo's release notes,
   and the skipped one and its reason go in the verdict file.
2. `tools/corpus/README.md` records where each row came from (the PR URL, and the author's "no
   functional change" statement, paraphrased).
3. `corpus.ps1 -Fetch` and `-List` handle `cleanup` rows as they handle `human` rows.
   `.claude/skills/equiv-corpus-run/SKILL.md` gains a "Cleanup pairs" subsection. It runs `full`
   (and `--execute` where the runtime is installed), uses the same SUMMARY template, and puts the
   results in a "Cleanup pairs" section of the verdict file, outside ADR 0028's rule table.
4. `docs/runs/<date>-cleanup-verdict.md` reports, per pair:
   - the detected runtimes (both sides, from `run.properties.runtimes`);
   - matched pairs and changed pairs;
   - changed pairs proved Equivalent (by `proofMethod`), Unknown by reason, and Divergent;
   - every Divergent adjudicated as P2-047 does: confirmed means the cleanup changed behaviour,
     false positive means a P2 ticket.
5. It gives no source text and no model values (`docs/runs/README.md`). A confirmed behaviour change
   is reported to the user by procedure identity before the PR is opened.

## Files
`tools/corpus/pairs.csv`, `tools/corpus/README.md`, `tools/corpus/corpus.ps1`,
`.claude/skills/equiv-corpus-run/SKILL.md`, `docs/runs/<date>-cleanup-<slug>/SUMMARY.md`,
`docs/runs/<date>-cleanup-verdict.md`, new `docs/tickets/P2-nnn-*.md`, `docs/ROADMAP.md`.

## Tests
`tools/corpus/tests` cases for a `cleanup` row in `-List` and `-Fetch`, if that suite covers `human` rows.

## Size guard
Any edit under `src/`: stop, that is a finding. More than five cleanup pairs: stop, since each
further pair is its own PR.

## Out of scope
Changing ADR 0028's thresholds. Agent-made cleanups (a new migration prompt is a separate decision).

## Notes
- Decision: `-List` and `-Fetch` needed no change for the `cleanup` kind. Both read `pairs.csv` rows
  without looking at `kind`, and all three rows fetched as written. `tools/corpus/tests` covers only
  `-Metrics` and `-Progress`, with no `human` row case, so the ticket's Tests section adds none.
- `corpus.ps1` did change, for a reason the ticket did not foresee: Git Extensions' later commits
  pin an SDK in `global.json` with `rollForward: feature` (8.0.0 and 6.0.401). `-Fetch` patched only
  a `global.json` with no `rollForward`, so both runs died in one second with "A compatible .NET SDK
  was not found". It now patches any policy that cannot cross a major.
- Decision: `powershell-19687` was kept, not replaced. Its first run skipped
  `System.Management.Automation` on the legacy side (exit 4), which the ticket would count as "does
  not load". Any other PowerShell pull request would hit the same three obstacles, so the fix was to
  clear them: `UseRidGraph=true`, a local tag for the build's `git describe`, and one run of the
  repository's `src/ResGen`. None edits a tracked file. They are written up in
  `tools/corpus/README.md`.
- Decision: a cleanup pair gets one plain `full` run. The `--fail-on unknown` run changes only the
  exit code, and nothing in a cleanup report uses it.
- Decision: a fourth adjudication class, "real by construction, not the cleanup", for the one
  Divergent that rests on a generated commit hash. Criterion 4's "confirmed" means the cleanup
  changed behaviour, which it did not, and the two builds do differ, so it is not a false positive.
- Decision: PowerShell's per-pair figures use the 12 pairs the pull request edited, with the 140
  the census counts stated beside them. The other 128 have identical source (P2-097).
- `--execute`: net8.0 is not installed here (only 6.0.36, 10.0.9 and 10.0.12), so the two net8.0
  pairs have no execution run. `gitextensions-11284` is net6.0 and was run with `--execute`.
- The `gitextensions-11372` run took 4h43m, of which two pairs took 2h19m. Another session's corpus
  run shared the box throughout. `gitextensions-11284` took 44 minutes.
- Git Bash mangles backslashes inside a quoted heredoc, so analysis scripts with `'\'` went into
  files instead.
- No confirmed behaviour change, so criterion 5's report to the user before the PR had nothing to
  carry.
