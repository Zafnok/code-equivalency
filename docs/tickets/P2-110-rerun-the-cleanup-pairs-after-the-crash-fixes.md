# P2-110 Rerun the three cleanup pairs once the lowering crashes are fixed, and refresh the cleanup verdict
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-105, P2-103, P2-104, P2-093, P2-127, P2-077, P1-032

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
   Divergent. It names which of P2-071, P2-098, P2-101, P2-103, P2-104, P2-106, P2-107 and P2-120
   were done at the commit that ran.
4. Every Divergent is adjudicated by P2-047's method. A cause with an open ticket is counted under
   it; a new cause is a new `P2-nnn` ticket. A confirmed behaviour change is reported to the user by
   procedure identity before the PR is opened.
5. `docs/ROADMAP.md` quotes the new totals where it quotes P2-058's.
6. `equiv-scoreboard` is applied: the README's cleanup numbers whose value changed are rewritten,
   and no other line of `README.md` is.

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
- 2026-10-04: P2-103, P2-104, P2-093, P2-127 and P2-077 were added to `Depends on`. They are the
  cleanup-relevant part of the scoreboard milestone P2-130 sets, so the rerun measures them too.
- 2026-10-13, the runs. equiv `78853202`, six runs one after the other from one build, on a box that
  ran nothing else, with `.corpus/` made new: the three pairs in compare mode quick (259 s, 229 s,
  183 s; exits 1, 1, 0), then in thorough (3846 s, 3357 s, 470 s; exits 1, 1, 0). No run has a
  lowering notification, a `run.properties.unverified` entry or exit 5.
- Decision: the ticket names no compare mode. `equiv-corpus-run` asks for thorough for a verdict and
  `equiv-scoreboard` takes only quick runs as a source, so both were run, as P2-124 did. The
  verdict's tables are the quick runs; the thorough runs have their own section.
- Decision: no `--jobs` (24 pairs at a time). No earlier run of these pairs is in a compare mode, so
  there is no time to compare with, and every timeout of the quick runs ends on the resource limit,
  not the wall clock.
- Decision: PowerShell's row and the totals count its 12 edited pairs, as P2-058's verdict did, so
  "before" and "now" are the same count (509 against 575). The verdict also gives the totals over
  all 37 of its changed pairs.
- Decision: `BugReporter.Program::Main()` keeps P2-058's classification, "real by construction, not
  the cleanup". It is Divergent on both Git Extensions pairs for its generated commit constant, which
  ADR 0046 keeps an ordinary constant on purpose. No ticket.
- Result, quick: of 509 changed pairs the solver proves 297 (58.3%; 57 of 575 before), 210 are
  Unknown and 2 Divergent (52 before). No confirmed behaviour change, so nothing was reported to the
  user under criterion 4.
- Thorough adds 3 EQ002 on `gitextensions-11372`, all from the IL pass on a collection expression
  with a spread, all false positives: P2-169, new.
- The skill's restore block does not fit these pairs: both Git Extensions sides and both PowerShell
  sides restore with `dotnet restore --force`. `corpus.ps1 -Packages` works again (P2-167).
- Not filed, because the criteria file a ticket for a crash and for a Divergent cause only:
  `DeconstructionAssignment` is in the reason set of 46 of `gitextensions-11284`'s 178 changed
  pairs (25.8%) and no open ticket owns it; 17 `unbound` pairs are left on PowerShell.
