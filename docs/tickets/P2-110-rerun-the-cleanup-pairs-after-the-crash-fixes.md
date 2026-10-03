# P2-110 Rerun the three cleanup pairs once the lowering crashes are fixed, and refresh the cleanup verdict
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-105

## Goal
`docs/runs/2026-10-02-cleanup-verdict.md` is P2-058's: three cleanup pairs at equiv `46e6636`, all
exit 5, 57 of 575 changed pairs proved. Since then each precision ticket reruns one pair against its
own fix (P2-099 reran `gitextensions-11372`: 160 of 351 proved, still exit 5), and no run covers all
three on one commit. Every run so far also has a pair with no verdict, because five procedures make
the lowerer throw (P2-105). Once P2-105 is done, run the three pairs again on one commit of `main`
and write the verdict again, so there is one current answer to "what does equiv say about a cleanup"
with no crashed pair in it.

## Spec references
`.claude/skills/equiv-corpus-run/SKILL.md` (the "Cleanup pairs" section);
`docs/runs/2026-10-02-cleanup-verdict.md` and the three `docs/runs/2026-10-02-cleanup-*/SUMMARY.md`;
`docs/runs/2026-10-03-cleanup-gitextensions-11372/SUMMARY.md`; ADR 0040 decision 5.

## Acceptance criteria (all must hold; nothing beyond them)
1. `gitextensions-11372`, `gitextensions-11284` and `powershell-19687` are each run in `full` mode
   on the same commit of `main`, one after the other, with a `SUMMARY.md` each.
2. No run has a lowering notification, an entry in `run.properties.unverified` or exit 5. If one
   does, record the procedure identity, reopen or file the crash ticket, and stop: the verdict is
   not written from a run with a crashed pair.
3. `docs/runs/<date>-cleanup-verdict.md` has the per-pair table of the 2026-10-02 verdict with a
   "before" column from it: changed pairs, proved Equivalent by `proofMethod`, Unknown by reason,
   Divergent. It names which of P2-071, P2-098, P2-101, P2-103, P2-104, P2-106, P2-107 and P2-109
   were done at the commit that ran.
4. Every Divergent is adjudicated by P2-047's method. A cause with an open ticket is counted under
   it; a new cause is a new `P2-nnn` ticket. A confirmed behaviour change is reported to the user by
   procedure identity before the PR is opened.
5. `docs/ROADMAP.md` quotes the new totals where it quotes P2-058's.

## Tests
None: no `src/` or `tests/` change. A crash, a load failure or a wrong verdict is a finding and a
ticket, never an edit during the run.

## Size guard
More than the three pairs, or any change under `src/`: stop, that is its own ticket.

## Out of scope
`--execute` runs; installing a runtime; the migration pairs.

## Notes
- Filed 2026-10-03 from P2-099's rerun, which exits 5 on
  `GitUI.UserControls.RevisionGrid.Graph.RevisionGraph::LoadingCompleted()`.
- Both Git Extensions sides are SDK-style net8.0: restore them with `dotnet restore --force`. The
  skill's `MSBuild -t:restore` fails on them with MSB4018.
- `gitextensions-11372` took 31 minutes on 2026-10-03 on a shared box, not the 4h43m of 2026-10-02.
