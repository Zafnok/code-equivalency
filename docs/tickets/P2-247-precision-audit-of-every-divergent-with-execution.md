# P2-247 Precision audit with execution: every Divergent of the five large pairs, P2-237's 71 undetermined and a fresh sample of runtime exposures
Status: in-progress
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-240, P2-241, P2-242, P2-245, P2-246

## Goal
The README's migration precision row is 11.1% (2 of 18), EQ002 and EQ006 only, over a sample P2-212 drew at
`bfbf413e` (`docs/runs/2026-10-17-runtime-exposure-verdict.md`): 25 of its sampled migration Divergents are still
undetermined, and P2-237 (`docs/runs/2026-10-17-precision-execute-verdict.md`) left 71 of P2-212's 90 undetermined,
almost all because no driver could call them (41 not callable, 10 an argument no driver builds, 8 OpenRA mod projects
under `probe`, 5 a constructor or a struct). Every one of its 37 EQ002 replays was `not-constructible`. The execution
reach tickets (P2-240 to P2-242, P2-245, P2-246) remove those obstacles. Of the 831 runtime exposures (EQ007, ADR 0060)
only 30 have been adjudicated (15 confirmed, 15 false positive), all witnessed: none of the 596 unwitnessed ones.

Divergent is now small (68 over the five pairs at `b0f56ff8`: EQ002 43, EQ006 25), so this audit draws no Divergent
sample: it runs the five large pairs with `--execute` once the reach tickets are merged and adjudicates every EQ002 and
EQ006, re-adjudicates P2-237's 71, and adjudicates a fresh sample of exposures. The verdict replaces P2-212's and
P2-237's as the adjudication of these pairs, and names "Scoreboard milestone 4".

## Spec references
`.claude/skills/equiv-corpus-run`, `.claude/skills/equiv-scoreboard`, ADR 0028, ADR 0035 (decision 2 and its
2026-09-28 clarification), ADR 0040 decision 3, ADR 0060 (decisions 4 and 7), ADR 0062,
`docs/runs/2026-10-15-precision-verdict.md` (P2-212: method, findings table), `docs/runs/2026-10-17-precision-execute-verdict.md`
(P2-237: method, its Decision lines, the appendix of the 90), `docs/runs/2026-10-17-runtime-exposure-verdict.md`
(P2-238: the re-split, the "now" rule), tickets P2-130, P2-212 and P2-237 (criteria and Notes).

## Acceptance criteria (all must hold; nothing beyond them)
1. The runtime. Before any run, `dotnet --list-runtimes` is recorded and checked against each pair's
   `run.properties.runtimes`. `Microsoft.NETCore.App` 5.0.17 was installed by P2-237 and kept at the user's word; if it
   is gone, it is reinstalled exactly as P2-237's criterion 1 says (the runtime installer only, from
   `dotnet.microsoft.com/download/dotnet/5.0`, SHA512 checked against the page, side by side, not the SDK, not the
   hosting bundle, not the desktop runtime), and anything else a side needs is asked for before it is installed;
   without it, that side records "execution unavailable: <runtime> not installed" and the audit continues. ADR 0040
   decision 3 never substitutes a runtime. Net6.0, net8.0 and net9.0 sides are checked the same way; a missing one is
   asked for, never installed unasked. Nothing but these runs and their probes runs on .NET 5.
2. `gitextensions-8522`, `gitextensions-9860`, `jellyfin-13023`, `duplicati-3124` and `openra-17989` are each run in
   `full` mode with `--execute`, compare mode quick, `--jobs 4`, with each pair's committed config, through
   `equiv-corpus-run`, one after the other from one Release build of one commit of `main`, with no other corpus run on
   the machine meanwhile (P2-248's included). Each has `docs/runs/<date>-full-execute-<slug>/SUMMARY.md` recording
   `run.properties.mode`, both sides' detected runtimes, wall-clock and exit code. A lowering notification, an entry in
   `run.properties.unverified` or exit 5 names its procedure and gets its crash ticket (filed or reopened); the verdict
   is written from the pairs that completed.
3. Replay counts per pair: every EQ002 and EQ006 carries `properties.replay`, and the verdict counts `reproduced`,
   `not-reproduced`, `not-constructible` and `not-applicable` per pair and rule, the `not-constructible` ones by
   `replayReason`, beside P2-237's (EQ002 0 of 37 constructible; EQ006 10 `reproduced`, 10 `not-applicable` of 260).
   EQ006 results that ADR 0060 decision 4 moved up from EQ007 (`proofMethod: observed`) are counted apart from the
   EQ006 a default run reports.
4. Every EQ002 and every EQ006 of the five runs is adjudicated, no sample, by P2-237's method and its Decision lines:
   **confirmed** needs an executed oracle (a `reproduced` replay, or `equiv mcp --execute`'s `probe` run on both
   runtimes showing the claimed difference at the outcome); **false positive** needs a hand trace showing the claimed
   difference cannot happen on the pair's runtimes; anything else is **undetermined**, with its one obstacle. A replay
   or `probe` that differs for another reason than the claimed one confirms nothing. A result P2-212, P2-237 or P2-245
   already classified, matched by procedure identity and rule, keeps that classification unless this run's execution
   contradicts it, and the appendix says which.
5. P2-237's 71 still-undetermined results are each re-adjudicated by criterion 4's method, matched by procedure identity
   and rule; one no longer flagged at the run's commit is listed with what it became and counted apart. The verdict
   gives, per obstacle class of P2-237's paragraph, how many were confirmed, refuted or remain undetermined.
6. A fresh exposure sample: on each of the three migrations, 15 witnessed and 15 unwitnessed EQ007 drawn at random
   (seed recorded; all of a class when it has 15 or fewer), excluding procedures in P2-212's sample. Each is
   adjudicated: **confirmed** when execution shows the named runtime change deciding the outcome (the model's input
   for a witnessed one, the row's own witness carried through the body for an unwitnessed one), **false positive** when
   a hand trace shows the change cannot reach the outcome on the pair's runtimes, else undetermined with its obstacle.
7. `docs/runs/<date>-precision-execute-verdict-2.md` gives: Divergent precision (confirmed / (confirmed + false
   positive)) per pair, per rule (EQ002, EQ006 as a default run reports it, observed EQ006 apart) and over the
   migrations and over the upgrades, beside the README's 11.1% (2 of 18) and 85.7% (6 of 7); exposure precision per
   migration, witnessed and unwitnessed apart, beside 15 of 30; the count and share of undetermined results per class;
   every still-undetermined result's obstacle, grouped; and an appendix row per adjudicated result (pair, procedure,
   rule, replay outcome, classification, the oracle or trace in one line).
8. Every false-positive cause, crash and `not-reproduced` replay names an owning ticket: an open one that owns the
   cause, or a new `P2-nnn` filed in this pull request (a `not-reproduced` replay is a soundness or modelling finding,
   ADR 0035 decision 2). A cause with several results is one ticket. The verdict's findings table lists each.
9. `equiv-scoreboard` is applied: only the README rows whose value changed are rewritten, and the pull request body
   lists each with its old and new value.
10. `docs/ROADMAP.md` gets "Scoreboard milestone 4 (<date>)" after milestone 3: the open tickets, the ones filed by
    criterion 8 included, ranked by the false positives, undetermined results and Unknowns this verdict counts against
    each (for Unknowns, P2-248's unlock table if it is done, else these runs' `changedReasonSets`), with the count on
    each line, as milestones 2 and 3 do. Milestone 3's line naming this ticket says it is done and points there.

## Files
`docs/runs/`, `README.md`, `docs/ROADMAP.md`, new ticket files (criterion 8). A throwaway script under the scratch
folder for the draw and the counts. Nothing under `src/`, `tests/` or `tools/`.

## Tests
None. This is an audit.

## Size guard
A change under `src/`, `tests/` or `tools/`, a sixth pair, a sample of Divergents, or more than 30 exposures on a pair
means the ticket was misread: record what you found and stop.

## Out of scope
Fixing anything a run or a trace finds. Thorough mode, `seeded` and `seeded-mech` runs. The upgrades' and cleanups'
exposures. Installing anything but what criterion 1 allows. ADR 0028's rule table: under ADR 0060 decision 7 nothing
here moves a criterion. The opaque and solver-budget counts (P2-248). A README edit to a row whose value did not change.

## Method
- Run time: P2-237's `--execute` runs took 488 s, 563 s and 272 s on the migrations, and P2-238's default runs of the
  upgrades about 5 and 3 minutes; reach tickets add driver builds. Never stop a run in progress.
- Volume: about 68 Divergents, 71 re-adjudications and 90 exposures. P2-237 split tracing and probing across one
  sub-agent per pair under one written brief and checked each table against `probe`'s own output; do the same, and
  record the brief's path in Notes.
- `probe` reloads both solutions per call (one to three minutes); batch the calls per pair.

## Notes
- Filed 2026-10-18. It is the "second execution re-adjudication of the 71" that milestone 3 names after P2-240,
  P2-241, P2-242 and P2-245; P2-246 is added because P2-242's size guard split the struct shapes into it, and 2 of the
  5 rows P2-242 owns need them.
- Decision: no Divergent sample -> 68 results over five pairs is fewer than P2-212's 143 sampled, and a full count has
  no sampling error, so the precision row stops resting on a sample that has drifted for two commits.
- Decision: exposure sample split 15 witnessed and 15 unwitnessed per migration -> the 30 adjudicated so far are all
  witnessed, so an even split measures the 596 unwitnessed ones without losing a comparison with the 15 of 30.
- .NET 5.0.17 is installed on this machine on 2026-10-18 (`dotnet --list-runtimes`).
- 2026-10-20, done at equiv `4b2e0df6` (`docs/runs/2026-10-20-precision-execute-verdict-2.md`): migration Divergent
  precision 52.2% (24 of 46) over every EQ002 and EQ006, upgrades 100% (6 of 6); P2-237's 71: 40 confirmed, 4 false
  positives, 27 undetermined; exposures witnessed 70.8% (17 of 24), unwitnessed 21.4% (6 of 28).
- Runtimes: the machine lacked `Microsoft.NETCore.App` 8.0 and 9.0 and `Microsoft.WindowsDesktop.App` 5.0 and 6.0. Asked,
  the user chose a private `dotnet` root in the session scratch folder over a machine-wide install (P2-253's method):
  Microsoft's runtime zips 8.0.32, 9.0.21 and Windows Desktop 6.0.36 (SHA512 equal to the release metadata's) and
  Windows Desktop 5.0.17 (no published hash; all 280 binaries Authenticode-signed by Microsoft). The machine install is
  unchanged.
- Decision: `gitextensions-8522`'s first run crashed (exit 5, no SARIF, P2-258), so it was rerun right after the other
  four from the same build without `Microsoft.WindowsDesktop.App` 5.0 (criterion 1's "execution unavailable"), and the
  verdict rests on the rerun. Alternative: write the verdict from four pairs. Rule: 2 (criterion 1 allows a side without
  a runtime; the audit loses 3 replays instead of a pair).
- Decision: an exposure is one result whatever its rule id (EQ006 in P2-212's sample, EQ007 or observed EQ006 now), as
  P2-245 matched them. Rule: 1 (criterion 4 matches by procedure identity and rule; ADR 0060 renamed the rule).
- Decision: a witnessed exposure is confirmed by the model's input where one can be built, else by the runtime row's own
  witness (no model input of the 35 could be built), and the appendix says which. Alternative: leave them undetermined.
  Rule: 1 (P2-237's 15 confirmed exposures, the comparison criterion 7 asks for, were confirmed the same way).
- Decision: a contradiction (criterion 4) includes an execution outside equiv that removes a kept confirmation's basis:
  P2-237's `SystemIOWindows::GetFileAttributes(string)` is a false positive now (P2-259, P2-260). Rule: 1.
- Decision: the scoreboard counts an observed EQ006 where a default run counts it, as a witnessed exposure. Rule: 1
  (`equiv-scoreboard`: a row rests on what the default mode reports; P2-238 did the same).
- Decision: Duplicati `SystemIOWindows::PathGetFileName(string)` is undetermined, not its sub-agent's false positive (a
  second runtime difference was seen under the harness and not checked). Rule: 1 (a false positive needs the claimed
  difference shown impossible, and the result's own difference is not shown absent).
- The sub-agents' brief: `brief.md` in the session scratch folder
  (`C:\Users\Zafno\AppData\Local\Temp\claude\D--code-equivalency--claude-worktrees-precision-audit-divergent-execution-fb5955\863d2093-4e51-42fc-98c0-450be77a8dc8\scratchpad\brief.md`),
  six sub-agents (one per migration for the Divergent and P2-237 rows, one per migration for the exposures), 174 `probe`
  calls.
- Surprises: the legacy replay driver runs .NET Framework's legacy path handling, so every `\\?\` path fails on its side
  (P2-259), which made 9 replays `reproduced` and 2 of P2-237's confirmations false; `probe`'s constructor key is the
  parameter types only (the brief had it wrong at first); OpenRA has only 5 witnessed exposures outside P2-212's sample;
  ICU ignores every C0 control but tab, line feed, vertical tab, form feed and carriage return.
- Filed P2-258 to P2-273 (the verdict's findings table).
