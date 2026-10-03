# P2-066 Version-upgrade pairs in the corpus: pin two public .NET-to-.NET upgrades and run them
Status: done (PR #371)
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-055, P2-056, P2-047 (the adjudication method)

## Goal
ADR 0040 makes `equiv` check any runtime pair, and a .NET-to-.NET version upgrade (net6.0 to
net8.0, say) is named there as a use case. Every pair in the corpus crosses from .NET Framework to
.NET, and P2-058 adds only same-runtime cleanups, so nothing measures an upgrade. Such a pair is the
purest in-place migration: most bodies are byte-identical, and the question is only which runtime
changes between the two versions reach the code. Pin two public upgrades:
- a **pure bump**, where the PR changes target frameworks and packages and almost no `.cs` file;
- a **bump with fixes**, where the PR also edits code for the new version's breaking changes.

Run both, and report how much `equiv` proves and whether every EQ006 it raises is a change that
really happened between the two versions.

## Spec references
ADR 0040 decisions 1 to 3 (detection, interval, execution); ADR 0028 decisions 1 to 3 (pinning,
nothing third-party in git; a public before-and-after is a `human` pair);
`.claude/skills/equiv-corpus-run/SKILL.md`; P2-058 (the cleanup-pair ticket this mirrors).

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/corpus/pairs.csv` gains two `human` rows, with full 40-character SHAs. The legacy commit is
   the merge commit's first parent. Each pair:
   - is a public GitHub repo under an OSI licence;
   - has a PR whose stated purpose is moving every C# project from one .NET (Core) version to a
     later one, with no feature work;
   - loads in MSBuildWorkspace on both sides.

   Look first at repos that move to each new .NET release in a single PR, such as PowerShell or Git
   Extensions after its .NET 5 migration. A candidate that does not load is replaced, and the
   skipped one and its reason go in the verdict file.
2. `tools/corpus/README.md` records each row's PR URL, the two target frameworks, and which of the
   two shapes it is (pure bump, or bump with fixes).
3. Each pair is run in the skill's `full` mode, then with `--execute` where both runtimes are
   installed. Each has `docs/runs/<date>-full-<slug>/SUMMARY.md` in the skill's template.
4. `docs/runs/<date>-upgrade-verdict.md` reports, per pair:
   - the detected runtimes, both sides, from `run.properties.runtimes`;
   - matched pairs and congruent pairs;
   - changed pairs, split into proved Equivalent (by `proofMethod`), Unknown by reason, and
     Divergent;
   - every EQ006 grouped by runtime-change row, with the row's `changedIn`.
5. Every EQ006 cites a row whose `changedIn` lies inside the pair's interval. Any that does not is
   a P2 ticket, because P2-055 then applied a rule it should not have.
6. Every EQ002 and EQ006 is adjudicated as P2-047 does. Confirmed means the upgrade changed
   behaviour; a false positive becomes a P2 ticket.
7. These pairs take no part in ADR 0028's rule table, and the verdict file says so. No source text
   or model value is committed (`docs/runs/README.md`). A confirmed behaviour change is reported to
   the user by procedure identity before the PR is opened.

## Files
`tools/corpus/pairs.csv`, `tools/corpus/README.md`, `docs/runs/<date>-full-<slug>/SUMMARY.md` (two),
`docs/runs/<date>-upgrade-verdict.md`, new `docs/tickets/P2-nnn-*.md` for findings, `docs/ROADMAP.md`.

## Tests
None, unless `tools/corpus/corpus.ps1` has to change to fetch a row; then a `tools/corpus/tests` case.

## Size guard
Any edit under `src/`: stop, that is a finding and becomes a ticket. More than three pairs: stop,
each further pair is its own ticket.

## Out of scope
A new pair kind (a `human` row with the shape in README is enough). Changing ADR 0028's thresholds.
Agent-made upgrades (a new migration prompt is a separate decision). Seeded runs.

## Notes
- Found by the 2026-09-30 goal review: the "in-place migration" goal includes version bumps, and
  nothing in the corpus is one.
- Pairs: `jellyfin-13023` (pure bump, net8.0 to net9.0) and `gitextensions-9860` (bump with fixes,
  net5.0 to net6.0). Both loaded on both sides, so no candidate was replaced.
- Decision: one pair per repository, not both from Git Extensions -> a server application and a
  WinForms one, so the two results are not one codebase's habits twice. Alternatives: Git Extensions
  #9860 and #11240. Rule: 3.
- Decision: `--execute` was not run -> criterion 3 asks for it where both runtimes are installed, and
  the box has .NET 6.0 and 10.0 only (no 5, 8 or 9, and no WindowsDesktop 6). Installing a runtime
  changes the machine and was not asked for.
- Decision: with no runtime for either side, an EQ002 is confirmed by the pull request's diff plus a
  test under `.corpus/audit/P2-066/` on net10.0 where the difference is in the code. P2-047 used each
  side's real runtime. Alternatives: call all five undetermined, which hides two changes the diff
  states outright. Rule: 3.
- Decision: an EQ006 on a row with no change point is not counted against criterion 5 -> ADR 0040
  decision 2 applies such a row whenever the runtimes differ, so P2-055 did what the ADR says. The
  rows themselves are the finding (P2-113).
- Toolchain: the first Jellyfin attempt died in 3 s on the checkout's `global.json` (SDK 8 pinned with
  `latestMinor`, only SDK 10 installed). P2-058 fixed `-Fetch` on main while this ticket was open; this
  PR adds the test for that patch (`tools/corpus/tests/Fetch.Tests.ps1`), which main did not have.
- Toolchain: `dotnet restore` of Jellyfin fails with NU1100 as the skill writes it; restored with
  `--configfile <this repo>/nuget.config` (P2-115).
- The first full runs were at `46e6636`. Jellyfin's was stopped by the user after 12h17m: one pair had
  spent 11h02m in `Z3Backend.Inline`, before any solver check (two `dotnet-stack` samples). P2-076
  merged, and both pairs were rerun at `8e0ed3c`: 1h32m and 1h11m. Git Extensions' two runs agree on
  every result's rule, proof method and runtime-change row.
- Deviation: criterion 3's run directories and the verdict file are dated 2026-10-03, the day of the
  reruns, not the day the ticket was started.
- P2-071 merged (8bf3aa1) after the reruns' build; one Jellyfin EQ002 it owns was not rerun.
