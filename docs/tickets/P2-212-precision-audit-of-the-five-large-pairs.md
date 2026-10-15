# P2-212 Precision audit: the five large pairs on one commit, adjudicated on fresh samples, and the README scoreboard from them
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-164

## Goal
The README's Divergent precision row is P2-124's and P2-130's: upgrades 40.0% (6 of 15) at `1d767f54`,
migrations 1.4% (1 of 69) at `e98fd1b9`. Since then the fixes for nearly every false positive those
audits counted have merged, and no run has measured them:
- migrations: P2-188, P2-189, P2-190, P2-200, P2-201 (path and runtime-row reach, 37 of the 68), P2-184
  (9), P2-192 (11), and P2-197 (69 OpenRA bodies under the .NET 9 float rule), P2-165 and P2-195 on the
  Unknown side;
- upgrades: P2-163 (4 EQ006) and P2-164 (5 EQ002), all 9 of the upgrades' false positives.

What is still open of the two audits' causes is a tail of one to six results each (P2-185, P2-186,
P2-187, P2-191, P2-193, P2-205 to P2-208, P2-097). P2-130's own Notes show why the old samples cannot
rank it: 48 of P2-047's 60 results were no longer Divergent when P2-130 re-adjudicated them, so a
sample drawn before the fixes describes results that no longer exist. Run the five large pairs at one
commit, adjudicate a fresh sample of each, and let that sample rank what comes next.

This is ROADMAP "Scoreboard milestone 2" item 7, filed before items 5 and 6 are done (2026-10-15, at
the user's request): the precision row is the scoreboard's weakest, and the remaining tail is small
enough that a measurement ranks it better than the old samples do.

## Spec references
`.claude/skills/equiv-corpus-run`, `.claude/skills/equiv-scoreboard`, ADR 0028, ADR 0034, ADR 0040,
`docs/runs/2026-10-12-upgrade-verdict.md`, `docs/runs/2026-10-14-migrations-verdict.md` (the
adjudication method, the sample sizes and the false-positive cause table), tickets P2-124 and P2-130
(their criteria and Notes), `docs/ROADMAP.md` "Scoreboard milestone 2".

## Acceptance criteria (all must hold; nothing beyond them)
1. `gitextensions-9860`, `jellyfin-13023`, `gitextensions-8522`, `duplicati-3124` and `openra-17989` are
   each run in `full` mode, in `compare`'s default mode (the summary records `run.properties.mode`),
   through `equiv-corpus-run`, one after the other, at one commit of `main` that holds P2-164. Each has a
   `docs/runs/<date>-full-<slug>/SUMMARY.md`.
2. No run has a lowering notification, an entry in `run.properties.unverified` or exit 5. If one does,
   record the procedure identity, file or reopen the crash ticket, and write the verdict from the pairs
   that completed, saying which did not.
3. `docs/runs/<date>-precision-verdict.md` has, per pair and next to the value in the last verdict
   (`2026-10-12-upgrade-verdict.md` or `2026-10-14-migrations-verdict.md`): matched pairs, congruent
   pairs, changed pairs, proved Equivalent by `proofMethod`, Unknown by reason, EQ002 and EQ006, and the
   `runtime-rule` count of `properties.notCongruent`.
4. Adjudication, by P2-130's method (a hand trace; a result whose difference cannot be shown either
   way is undetermined, not a false positive): on each upgrade pair, every EQ002 and EQ006; on each
   migration pair, a fresh draw of 30 EQ002 and 30 EQ006, or every one where a pair has fewer. A result
   P2-124 or P2-130 adjudicated whose procedure and cause are unchanged keeps its classification without
   a new trace. The verdict gives Divergent precision per pair and per class (upgrades, migrations),
   with the undetermined count beside it.
5. The verdict has one table that sets the fixes against the old audits: for each of P2-163, P2-164,
   P2-184, P2-188, P2-189, P2-190, P2-192, P2-197, P2-200 and P2-201, the false positives its Goal
   claimed and what became of each of those results (congruent, proved, Unknown with reason, still
   Divergent).
6. Each false positive names the open ticket that owns its cause or a new `P2-nnn` ticket. Each crash,
   and each opaque reason at or above 5% of a pair's changed pairs with no open ticket, is filed as a
   ticket.
7. `equiv-scoreboard` is applied over the five large pairs: each row whose value changed is rewritten,
   and no other line of `README.md` is. The pull request body lists the rows that moved, old value and
   new.
8. `docs/ROADMAP.md` gets a "Scoreboard milestone 3" section after milestone 2: the date, the commit,
   the verdict file, and the open tickets ranked by the false positives and Unknowns this verdict counts
   against each. Milestone 2's item 7 says it was done by this ticket.

## Files
`docs/runs/`, `README.md`, `docs/ROADMAP.md`, new ticket files (criterion 6). Nothing under `src/`,
`tests/` or `tools/`.

## Tests
None.

## Size guard
A change under `src/`, `tests/` or `tools/`, or a sixth pair, means the ticket was misread: file what
you found.

## Out of scope
The cleanup pairs (P2-110's verdict has no Divergent left to decide). `--execute`, `seeded` and
`seeded-mech` runs. Thorough mode. Fixing anything a run finds. A README edit to a row whose value did
not change.

## Notes
- Filed 2026-10-15, while P2-164 was in review (PR #508). P2-211 is filed by that pull request; this
  ticket takes the next free id.
- Last recorded wall-clock at `--jobs 4`: P2-130's three migrations 413 s, 547 s and 292 s (2026-10-14);
  the upgrades 4 min and 3 min (2026-10-12). The default `jobs` is the processor count since P2-132;
  P2-130 ran at `--jobs 4` so the run-time row keeps one meaning. Never stop a run in progress.
- Ran 2026-10-15 17:05 to 17:29 at `bfbf413e` (`main`, holding P2-164), compare mode quick, `--jobs 4`, one pair at a
  time from one build: 271 s, 168 s, 363 s, 447 s and 217 s, every run exit 1, no crash. `.corpus/` was fetched and
  restored new in this worktree.
- Decision: the fresh draw is P2-130's method with seed 212 (the ticket number, as P2-047 used 47), so it is a new
  draw and not P2-130's sample again.
- Decision: a result P2-124 or P2-130 adjudicated is matched by procedure identity and rule, not by fingerprint, which
  moves between commits for the same procedure. Its cause counts as unchanged, and its classification is kept, when it
  was confirmed or undetermined, or a false positive whose owner is still open; one whose owner is done was traced again.
- Decision: P2-192's 9 sampled results keep their classification without a trace: they are the procedures P2-192's
  Notes traced, and the run passes no declaration. They are counted under P2-213 (new), which commits the declaration
  the way P2-197 committed OpenRA's runtime. `PatchManager::ToPatch(...)` is counted under P2-211, whose Goal traces it.
- Decision: P2-130's definition of confirmed (an executed oracle) applies to new results; P2-124's six confirmed results
  keep their class. Nothing could be executed (no replay; the box has .NET 6.0 and 10.0 only).
- The 61 new hand traces were done by three sub-agents, one per migration pair, under one brief; their tables were
  read in full and each cause checked against the open tickets before filing P2-213 to P2-220.
- Found while running: `corpus.ps1 -Unchanged` prints its table to the host, not the success stream, so a script that
  captures output gets nothing; the summaries' numbers were read from the console. Too small for a ticket; recorded here.
