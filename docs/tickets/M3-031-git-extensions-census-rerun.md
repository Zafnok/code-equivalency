# M3-031 Census rerun on Git Extensions, scored under ADR 0034
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-030, P2-010, P2-011, P2-012

## Goal
ADR 0034 and ADR 0028. M3-022's verdict is "incomplete: human pair pending": Git Extensions
crashed in lowering, and the three agent pairs are pure retargets with no changed pair. This
ticket reruns the census on the `gitextensions-8522` human pair, which is the feasibility test,
and uses the exact reason-set counts to order M4. Fix nothing.

## Spec references
ADR 0034; ADR 0028 decision 5; ADR 0027 decision 3; `docs/runs/2026-09-23-census-verdict.md`;
`.claude/skills/equiv-corpus-run/SKILL.md`; `docs/tickets/done/M3-022-real-pair-census.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Run the `equiv-corpus-run` skill in `census` mode on `gitextensions-8522`, and rerun
   `corpus.ps1 -Packages` and the census on the three agent pairs M3-022 used, reusing their
   existing modern sides in `.corpus/` (no new agent migration). A pair that no longer loads is
   recorded with the reason, not replaced.
2. Each pair has `docs/runs/<yyyy-mm-dd>-census-<slug>/SUMMARY.md` in the skill's template,
   including the `## Changed code` section M3-030 adds.
3. `docs/runs/<yyyy-mm-dd>-census-verdict.md` applies ADR 0028's rules as amended by ADR 0034:
   lowerable share over changed pairs, agent pairs with zero changed pairs excluded from the
   median, and Git Extensions alone deciding the lowerable-share rules when fewer than three agent
   pairs remain, stated in the file. It states the outcome: continue, re-scope or stop. If the
   outcome is re-scope or stop, stop here and tell the user. That needs a new ADR, not more
   tickets.
4. Reorder the M4 list in `docs/ROADMAP.md` by pairs unlocked (ADR 0034 item 2) per effort point
   (S=1, M=2, L=4). Soundness dependencies still win. Ticket ids are not renumbered. An M4 ticket
   below ADR 0028's 5% bar, now 5% of changed pairs, moves to the post-MVP backlog, and M4-007's
   `Depends on:` line drops it. A P2 ticket from M3-022 that clears the bar is scheduled into M4.
5. `M3-022`'s Notes gain one line pointing at this verdict.
6. No changes under `src/` or `tests/`.
7. The `gitextensions-8522` census completes (writes its SARIF, not exit 1), and the SARIF has no
   lowering-exception result from `IrLowerer.Destination` (P2-010 criterion 3, moved here). If one
   remains, record it in the SUMMARY's Findings and file a P2 ticket. Do not fix it here.

## Size guard
If you are editing engine code or `corpus.ps1`, stop; that is a new ticket.

## Out of scope
Verification (`full` and `seeded` modes are M4-007). New agent migrations. Fixes for anything the
run finds: those become P2 tickets, as the skill requires.

## Notes
- Criterion 7 is carried over from P2-010 (PR #155). That fix could not be checked on the corpus
  because the loader is Windows-only and P2-010 was done on Linux. P2-010's unit tests pin the
  cause: a `try` whose `finally` never completes (always throws, or loops forever).
