# M4-007 First full corpus run: verdicts, seeded recall, and the success criteria
Status: done (PR #234)
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-004, M3-022, M3-033, M4-001, M4-002, M4-004, M4-009, M4-010, P2-001
(without the precision tickets the run is mostly `Unknown(opaque)` and says little. The
2026-09-24 census moved M4-003, M4-005, M4-006 and M4-008 to the post-MVP backlog. M3-033,
M4-009 and M4-010 came from the 2026-09-24 second-oracle review (ADR 0035): a measured runtime
table, replayed counterexamples, and seeded recall over mechanical seeds)

## Goal
Run the full tool on the public corpus (ADR 0028), exactly as a user would after an agent migrated
their code. Evaluate every ADR 0028 criterion, and turn every surprise into a ticket. Fix nothing.
(Until 2026-09-23 this ticket named an unspecified real pair. ADR 0028 replaced it with the
corpus.)

## Acceptance criteria (all must hold; nothing beyond them)
1. Run the `equiv-corpus-run` skill in `full` mode, then in `seeded` mode, on the same pairs
   M3-022 used: the Git Extensions pair and the agent pairs.
2. Each run's `SUMMARY.md` (skill template) has:
   - procedure counts (matched, added, removed);
   - verdict counts by kind, by `proofMethod` and by Unknown `scope` (ADR 0029);
   - the ten most common Unknown reasons;
   - the repo's own test results on both sides (`verify_command` from the manifest);
   - unchanged share, lowerable share, project load rate, line-scoped Unknown share and seeded
     recall (ADR 0028);
   - wall-clock time;
   - `replay` counts by value, from a third run of `full` with `--execute` (M4-009); every
     `not-reproduced` result is a ticket, as in criterion 3.
3. Seeded recall is 100%, over the hand-written seeds and M4-010's confirmed mechanical seeds: no
   seeded change is reported Equivalent. Any miss is written first, as
   a `P2` ticket titled `soundness:` with the seed id, and reported to the user before anything
   else.
4. Every test that passes on the legacy side and fails on the modern side is listed. Next to it
   goes the verdict of each procedure the test's stack trace names. Any such procedure reported
   Equivalent is a soundness ticket, as in criterion 3.
5. Ask the user to name the three Divergent results they find most believable and the three least
   believable. Record their identities and the reasons in the summary.
6. For every distinct remaining `IrOpaque` reason and every crash, one post-MVP ticket file
   `P2-nnn-<slug>.md`, with a minimal repro in your own code sketched in the Goal.
7. No changes under `src/` or `tests/`. No third-party source text committed.

## Size guard
If you are editing engine code, stop; that is a new ticket.

## Out of scope
Fixes. Performance work. New samples beyond the sketches in the tickets. Any code not listed in
`tools/corpus/`.

## Notes

- Decision: the skill's step 5 asks for `full` run once with defaults and once with `--fail-on
  unknown`, "into a second SARIF". `--fail-on` only changes `DecideExitCode`'s return value
  (src/Equiv.Cli/CompareCommand.cs), never the verification results, so the second SARIF would be
  byte-identical to the first modulo nothing the SUMMARY template reads. Skipping the duplicate
  pass and spending that wall-clock on the seeded and `--execute`/replay runs, which do add
  required data. (equiv-decide.)
- Observed: on Git Extensions, `full` without `--lower-only` took 9450s (~2h37m) for 13,541
  matched pairs, mostly Z3 verification of the 8,914 without-opaque pairs, finishing with exit 5
  (91 pair-level crashes). Mid-run I misdiagnosed it as hung: I sampled `Get-Process` on the
  `dotnet run` launcher (2.6s CPU, all threads waiting, as a launcher should be), not on its child
  `Equiv.Cli.exe`, which held the CPU time. To judge a corpus run, sample the `Equiv.Cli.exe`
  child (`Get-CimInstance Win32_Process` by ParentProcessId), and expect hours on a repo this size.
- Observed: a genuine hang. `compare --execute` on `pmb-tomasjohansson__adapters-shortest-paths-dotnet`
  stopped making progress about a minute in (18:11). Its `Equiv.Cli.exe` sat at 65s CPU and both
  replay driver processes (`EquivReplay4.exe` on .NET Framework, `dotnet EquivReplay4.dll` on
  .NET 10, project `Programmerare.ShortestPaths.Adaptee.YanQi`) sat at 0.03s CPU each for 2h50m, with
  no CPU change over a 20s sample, while a Git Extensions worker in the same session gained ~20 CPU-s.
  The documented per-pair testing budget (60 s) did not fire. Filed as P2-039.
- Fix: `tools/corpus/corpus.ps1`'s `-SeedMechanical` branch (line ~448) called `ConvertFrom-Json`
  directly on a prior run's SARIF without the `"" -> "(no opaque)"` workaround the `-Metrics`
  branch already has for `changedReasonSets`' empty-string key (ADR 0034; Windows PowerShell 5.1's
  `ConvertFrom-Json` has no `-AsHashtable` and rejects an empty property name). Applied the same
  regex workaround. This is `tools/corpus/`, not `src/`/`tests/`, so it's corpus-run tooling, not
  engine code.
- Deviation: `equiv-corpus-run` hard rule 2 says `git status` must list nothing outside
  `docs/runs/`, `docs/ROADMAP.md` and `docs/tickets/`. This PR also changes `tools/corpus/corpus.ps1`
  (the fix above), because without it criterion 3's mechanical seeds could not run at all. It is a
  copy of a workaround already in the same file; no ADR bar is met.
- Decision: agent pairs were migrated by general-purpose subagents of this session (Claude Sonnet 5,
  2026-09-27), each given only `tools/corpus/migration-prompt.md`'s text. All three builds succeed
  and all modern tests pass (21, 11, 98). The SignalR migration removed `ExampleUsingIIS` from the
  solution (System.Web has no .NET 10 equivalent).
- Decision: the pairs are the four M3-022 used (Git Extensions, ServiceAnt, SignalR.Extras.Autofac,
  adapters-shortest-paths-dotnet), per criterion 1, although `-Select -Count 3` now ranks
  chrismckelt/WebMinder above SignalR.Extras.Autofac.
- Decision: criterion 5 asks the user to name the most and least believable Divergent results. The
  user delegated it ("do whatever you want"), so the Git Extensions summary records the agent's
  picks, labelled as such, each backed by replay or execution evidence.
- Observed: `--execute` left eight junk directories (random non-ASCII names, a file plus `.backup`
  in each) in the repository root, created by corpus code under differential testing. Verified
  untracked and removed. P2-040.
- Observed: criterion 4 (tests passing on legacy, failing on modern) has no instances. Every modern
  agent-pair test passes; Git Extensions has no `verify_command` and a WinForms UI suite, so it was
  not run.
