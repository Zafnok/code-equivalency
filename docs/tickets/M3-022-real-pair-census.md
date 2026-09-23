# M3-022 Census of the public corpus, and ordering the precision work by it
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-014, M3-024

## Goal
ADR 0027 decision 3 and ADR 0028. Measure lowering on public migration pairs now, instead of at
M4-007. Evaluate ADR 0028's unchanged-share and lowerable-share rules against their fixed
thresholds, and use the histogram to order the M4 precision tickets. Fix nothing. (Until
2026-09-23 this ticket named an unspecified real pair. ADR 0028 replaced it with the corpus.)

## Acceptance criteria (all must hold; nothing beyond them)
1. Run the `equiv-corpus-run` skill in `census` mode on:
   - the `gitextensions-8522` human pair;
   - at least three Poly-MigrationBench repos migrated by an agent (agent pairs), chosen by the
     skill's selection rule.

   A repo that does not restore or load on this box is replaced by the next one in the selection
   order, with the reason recorded.
2. Each pair has `docs/runs/<yyyy-mm-dd>-census-<slug>/SUMMARY.md`, written in the skill's template:
   - procedure and matched-pair counts, and the analysed line counts of each side (M3-014);
   - `projectsSkipped` with reasons (M3-024);
   - `pairsWithoutOpaque`, `pairsWholeBodyOpaque` and `pairsCongruent` (if M3-015 has landed), each
     as a count and a percentage of matched pairs;
   - unchanged share, lowerable share and project load rate, as ADR 0028 defines them;
   - the top fifteen opaque reasons with counts, each mapped to the ticket that removes it, or
     "none";
   - wall-clock time.

   Raw SARIF and checkouts stay in `.corpus/`. No third-party source text is committed.
3. `docs/runs/<yyyy-mm-dd>-census-verdict.md` applies ADR 0028's rules to the Git Extensions pair
   and to the median of the agent pairs, and states the outcome: continue, re-scope or stop. If the
   outcome is re-scope or stop, stop here and tell the user. That needs a new ADR, not more
   tickets.
4. Reorder the M4 list in `docs/ROADMAP.md` by pairs unlocked per effort point (S=1, M=2, L=4).
   Soundness dependencies still win. Ticket ids are not renumbered; the list order is the order.
   An M4 ticket below ADR 0028's 5% bar moves to the post-MVP backlog, and M4-007's `Depends on:`
   line drops it.
5. For every reason in the top fifteen that no ticket covers, write one `P2-nnn-<slug>.md` with a
   minimal repro sketched in its Goal, in your own code, never copied from the corpus.
6. No changes under `src/` or `tests/`.

## Size guard
If you are editing engine code, stop; that is a new ticket.

## Out of scope
Verification. Fixes. Any code not listed in `tools/corpus/`.

## Notes
