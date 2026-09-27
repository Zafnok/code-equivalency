# M4-015 Corpus runs log at `debug`, report progress on demand, and time each phase in SUMMARY.md
Status: todo
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-012, M4-013, M4-014

## Goal
Our own corpus runs are the long ones, so they always log at `debug` to a file beside the SARIF,
and anyone can ask a run in progress where it is without touching the process. The skill's
SUMMARY.md then says where the wall-clock time went.

## Spec references
ADR 0038; `.claude/skills/equiv-corpus-run/SKILL.md` section 5 and the SUMMARY template.

## Acceptance criteria (all must hold; nothing beyond them)
1. Every `equiv compare` invocation in the skill passes `--verbosity debug --log "$run/progress.log"`.
2. `./tools/corpus/corpus.ps1 -Progress <runDir>` reads only `progress.log`. It prints the current
   phase, done/total, the last ETA and worst-case bound, the current item and how long it has run,
   and the five slowest items finished so far. It never opens a handle on the `equiv` process, and
   it opens the log for reading with `FileShare.ReadWrite`.
3. The skill says how to watch a run: `corpus.ps1 -Progress <runDir>`, or
   `Get-Content "$run/progress.log" -Wait -Tail 20`.
4. The SUMMARY.md template gains a `## Phase times` table (phase, items, seconds, ETA error at 50%)
   that `-Progress -Summary <runDir>` fills in from the phase-end lines.
5. A new Pester test, `tools/corpus/tests/Progress.Tests.ps1` (the first test for `corpus.ps1`), parses a checked-in synthetic
   `progress.log` fixture containing only made-up identities, and checks criteria 2 and 4.

## Files
- `tools/corpus/corpus.ps1`
- `tools/corpus/tests/Progress.Tests.ps1`, `tools/corpus/tests/fixtures/progress.log` (new)
- `.claude/skills/equiv-corpus-run/SKILL.md`

## Tests
- `Progress_Reports_Current_Item_And_Eta`, `Summary_Fills_Phase_Times` against the fixture

## Size guard
Any change under `src/` or `tests/`: stop, that belongs in M4-012 to M4-014.

## Out of scope
Changing thresholds or the metrics in ADR 0028. Re-running M4-007.

## Notes
