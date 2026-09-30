# P2-061 Three behaviour-preserving mechanical seeds on Git Extensions are reported Divergent
Status: in-progress
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
- Size guard: not P2-031/P2-032 sort handling. All three seeds really change behaviour; `equiv` is right each time.
- `.corpus/` is not on this box, so the seeded methods were read from Git Extensions' public source at the modern
  commit (5190ba5) and each seeded shape reproduced standalone in `PreservingSeedDivergenceTests`
  (`Equiv.Tests.Integration`), with the WinForms and tree-node types as a separate assembly.
- S186 (`Commute`), criterion 1: the method builds a command line from a `+` chain of `string` operands, and the seed
  swapped two of them. The two IRs call `string.Concat` with the operands in a different order, so the result and the
  argument passed on to the process start differ. Classification: **seeder bug** (`string`'s `+` is concatenation, not
  commutative).
- S177 (`Commute`), criterion 1: the method returns a `+` of two `string` properties of `this` (then a third,
  null-conditional operand). The seed swapped the first two: the getter calls come in the other order in the call
  trace and the concatenation's operands are swapped. Classification: **seeder bug**, same cause as S186; the
  getter-order half alone would be one too (ADR 0018: the call trace is observable).
- S150 (`InlineTemporary`), criterion 1: the method is one statement, a WinForms control field's `Enabled` set to
  another control field's `Checked`. With the temporary, the `Checked` getter is called before the receiver field is
  read; inlined, the field is read first. The getter is an external call, which reads and writes every `field.*` map
  (ADR 0018), so the temporary side reads the field from the map the getter wrote and the inlined side from the one
  before it: a getter that reassigns the field changes which control is enabled. Classification: **correct**. The seed
  changes behaviour whenever the getter can write that field, and `equiv` cannot rule that out, so the precondition is
  tightened.
- Fix (criterion 2): `Commute` now binds the method against the BCL (as the P2-048 operators do) and offers a site
  only when the operator is built-in and not on `string` or a delegate, and every name in both operands is a local,
  parameter or field (no member access, so no property getter), with every nested operator built-in and not on
  `string`. `IntroduceTemporary`/`InlineTemporary` offer an assignment only when its target is a bare name, so no
  receiver is read before the value. Tests: `SyntaxMutatorTests` (seeder) for the sites kept and refused;
  `PreservingSeedDivergenceTests` shows each seeded shape Divergent and the sites still offered (an integer sum
  commuted, a temporary before a bare-name target) Equivalent.
- Deviation: criterion 3 (re-run Equivalent for each seeder-bug seed) cannot hold. The two `Commute` seeds really change
  behaviour, so an Equivalent verdict on them would make `equiv` unsound (as in P2-036). After the fix the seeder no longer
  produces any of the three seeds, which `SyntaxMutatorTests` shows on the same shapes. No re-run on the corpus either:
  `.corpus/` is not on this box, so the next `-SeedMechanical` run on Git Extensions (P2-047 or later) confirms it.
- Decision: `Commute` rejects every member access, a field one included, because the seeder's BCL-only model cannot tell
  a corpus field from a property, and a null receiver makes a field read throw. It costs `Commute` sites, never
  soundness; PairGen's operands are locals, parameters and a static field, so the differential gate keeps its sites
  (`DifferentialSoundnessTests` and `PairGenTests` pass).
- Decision: `object`'s reference `==` still commutes: it is built-in and calls nothing.
