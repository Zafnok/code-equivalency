# P2-061 Three behaviour-preserving mechanical seeds on Git Extensions are reported Divergent
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-046

## Goal
P2-046 ran M4-010's mechanical seeds on Git Extensions for the first time (P2-035 fixed the seeder).
Of 194 Preserving seeds, 136 were Equivalent (70.1%). Three came back Divergent (EQ002) although the
same method, unseeded, is Equivalent (EQ001), so the seed itself changed the verdict, and a
Preserving operator cannot change behaviour: `equiv` disagrees with two methods that are equal by
construction. The seeds, by `.corpus/pairs/gitextensions-8522/seeded-mech/seeds.json`:
S150 `GitUI.CommandsDialogs.FormDeleteTag::EnableOrDisableRemotesCombobox()` (`InlineTemporary`),
S177 `GitUI.BranchTreePanel.RepoObjectsTree.SubmoduleNode::DisplayText()` (`Commute`),
S186 `GitUI.Script.PowerShellHelper::RunPowerShell(string,string?,string,bool)` (`Commute`).
(Four more Preserving seeds are Divergent too, but their unseeded methods already are, so the seed
is not the cause: S001, S109, S182, S247.) P2-036 is the precedent: it found a seeder bug, not an
engine bug. Find the cause for each, fix it where it lies (the seeder or the engine), and
re-run the three seeds.

## Spec references
`tools/corpus/seeder/seeds.md` (the Preserving family), P2-036 (its Notes), ADR 0018 (the call trace
is observable), ADR 0026 (Divergent only when untainted).

## Acceptance criteria (all must hold; nothing beyond them)
1. For each of S150, S177 and S186, the difference between the seeded and the unseeded method's lowered
   IR (or its call trace) is found and named in `## Notes`, without quoting corpus source.
2. Each is classified in Notes as **seeder bug** (the operator changed behaviour: fix the operator
   with a test in `tests/Equiv.Corpus.Seeder.Tests`), **engine bug** (fix with a unit test in the
   affected project using a sample under `samples/`), or **correct** (the seed does change
   behaviour, as P2-036 found for an argument-text case: then the operator's precondition is
   tightened, with a test).
3. Re-running the three seeds' methods (`equiv compare` on a copy under `.corpus/`) gives Equivalent
   for every one classified engine bug or seeder bug; the run's SARIF stays under `.corpus/`.

## Files
Depends on the classification: `tools/corpus/seeder/` and its tests, or the affected `src/` project and
its tests, and `samples/` if an engine bug needs a reproduction.

## Tests
Named after the classification, in the affected project.

## Size guard
If the same root cause is P2-031/P2-032-style sort handling, it was fixed there; recheck the seeds on
current `main` first and close this ticket as fixed.

## Out of scope
The four Divergent seeds whose unseeded method is already Divergent (adjudication is P2-047), and the
Preserving Unknown share (38 of 194).

## Notes
- Found by P2-046 (`docs/runs/2026-09-30-full-gitextensions-8522/SUMMARY.md`, mechanical seeds).
