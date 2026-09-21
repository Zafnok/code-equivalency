# M3-022 Census of the real pair, and ordering the precision work by it
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-014

## Goal
ADR 0027 decision 3. Measure the opaque-reason histogram on a real 4.8-to-10 solution pair now,
instead of at M3-005. Use it to order M3-010, M3-011 and M3-015 to M3-021. Fix nothing.

## Acceptance criteria (all must hold; nothing beyond them)
1. Ask the user for the two solution paths and whether the code is private. Do not proceed without them.
2. Run `equiv compare --lower-only`. Save the SARIF under `docs/runs/<yyyy-mm-dd>-census-<slug>/`,
   git-ignored if the code is private.
3. Write `SUMMARY.md` there with:
   - procedure and matched-pair counts;
   - `pairsWithoutOpaque`, `pairsWholeBodyOpaque` and, if M3-015 has landed, `pairsCongruent`,
     each as a count and a percentage of matched pairs;
   - the top fifteen opaque reasons with counts, each mapped to the ticket that removes it, or
     "none";
   - wall-clock time.
4. Reorder the "Precision" list in `docs/ROADMAP.md` by pairs unlocked per effort point
   (S=1, M=2, L=4). Soundness dependencies still win.
5. For every reason in the top fifteen that no ticket covers, write one `P2-nnn-<slug>.md` with a
   minimal repro sketched in its Goal.
6. No changes under `src/` or `tests/`.

## Size guard
If you are editing engine code, stop; that is a new ticket.

## Out of scope
Verification. Fixes.

## Notes
