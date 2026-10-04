# P2-082 Weighing a pair for the progress log can no longer end the run
Status: done (PR #376)
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-065 ran the two other large human migrations, `openra-17989` and `duplicati-3124`. Both `full`
runs, and both `full --execute` runs, load and lower every pair and then die with exit 5 and **no
SARIF**: 10,114 and 6,275 pairs without one verdict. The message is a bare "Object reference not
set to an instance of an object."

The stack, captured with a startup hook that logs first-chance exceptions, is the same on both pairs:
`IrLoopAnalysis.Search` (`IrLoopAnalysis.cs` line 102), from `IrLoopAnalysis..ctor`, `IrLoopAnalysis.Of`,
`PairWeight.Loops`, `PairWeight.Of`, `CompareCommand.Weighed` (`CompareCommand.cs` line 581),
`CompareCommand.Verified` (line 542). Line 102 reads a block's terminator's successors.

Two things are wrong, and both need fixing:
1. `Verified` weighs every pair for the progress log (ADR 0038) before its per-pair `try`, so one
   throwing pair ends the whole run. ADR 0023 says a crashing pair does not end the run.
2. Some lowered procedure makes `IrLoopAnalysis` dereference a null. The frontend produced IR that
   the analysis does not expect; which procedure and which construct is not known yet.

A `--lower-only` run of either pair does finish, so the procedure lowers without throwing.

## Spec references
ADR 0023 (a crashing pair does not end the run), ADR 0038 (the progress log and pair weights),
`src/Equiv.Cli/CompareCommand.cs` (`Verified`, `Weighed`), `src/Equiv.Core/Progress/PairWeight.cs`,
`src/Equiv.Core/Ir/IrLoopAnalysis.cs`, `src/Equiv.Core/Ir/IrValidator.cs`,
`docs/runs/2026-10-01-migrations-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Find the procedure: run `equiv compare --lower-only` on `openra-17989` (`equiv-corpus-run`, census
   mode) with whatever diagnostics name the pair `PairWeight.Of` throws on, and write its identity and
   the construct in `## Notes` (identity and operation kinds only, no corpus source). Do the same for
   `duplicati-3124`, or say it is the same construct.
2. A unit test lowers a C# method written for the test with that construct. Before the fix,
   `IrLoopAnalysis.Of` throws on its IR or `IrValidator.Validate` rejects it.
3. The frontend lowers the construct to IR that `IrValidator.Validate` accepts, or to an `IrOpaque`.
   If `IrValidator` accepted the bad IR, it gains the check.
4. A pair whose weighing throws is a pair-level failure like any other: a `toolExecutionNotification`,
   an entry in `properties.unverified`, every other pair verified, and the SARIF written. A test in
   `tests/Equiv.Cli.Tests` proves it with a backend or a procedure that makes the weighing throw.
5. `full` runs of `openra-17989` and `duplicati-3124` write a SARIF. Their exit codes and by-rule counts
   go in `## Notes`.

## Files
`src/Equiv.Cli/CompareCommand.cs`, `src/Equiv.Frontend.CSharp/Lowering/` (the construct),
`src/Equiv.Core/Ir/IrValidator.cs` (only if criterion 3 says so), their tests.

## Tests
`tests/Equiv.Cli.Tests` (criterion 4), `tests/Equiv.Frontend.CSharp.Tests` (criteria 2 and 3).

## Size guard
If the two pairs throw on unrelated constructs, fix the one on OpenRA and file the other.

## Out of scope
P2-083's lowering crashes on the same pairs. Changing how a pair is weighed.

## Notes
- Found by P2-065 (`docs/runs/2026-10-01-full-openra-17989/SUMMARY.md`,
  `docs/runs/2026-10-01-full-duplicati-3124/SUMMARY.md`), at equiv ef79ff6.
- Git Extensions finished `full` runs at bd8e379 (P2-046) and 1d4569a (P1-018). Whether it still does
  at ef79ff6 was not checked.
- P2-090 fixed one way the frontend built a block with no terminator: a branch on a compile-time constant
  (`const bool` flag) whose dead edge named a block that was never lowered. A Release build does not validate, so
  that procedure reaches `IrLoopAnalysis.Search` and throws at this ticket's line 102. Rerun with P2-090 in before
  hunting for the construct; criterion 1 still has to name the pair, and criteria 3 (the validator check) and 4 stand.
- In progress (2026-10-02), branch `P2-082-pair-weighting-crash`. Done: criterion 4 (`Verified` catches a throw from
  deciding or weighing a pair; `CompareCommandTests.Compare_PairWhoseWeighingThrows_IsReportedAsNotificationAndOtherPairsVerified`)
  and criterion 3's validator half (`IrValidator` reports a block with no terminator as IR014;
  `IrValidatorTests.IR014ABlockWithoutATerminator`). Open: criteria 1, 2 and 5.
- A `full` run of `openra-17989` at this branch (P2-090 in) was started and stopped by hand at verify 151 of 10114,
  3 minutes 20 seconds in, because its own estimate was about two hours. Up to there: no lowering failure, no weighing
  failure, 145 congruent, 2 divergent, 4 unknown. It got past the point where ef79ff6 died, but it did not finish, so it
  says nothing yet about the remaining pairs. The pair checkouts are restored in the P2-065 worktree's `.corpus/`.
- Next: rerun `full` on both pairs to the end (criterion 5). A pair that still fails at weighing is named by its
  `Weighing ... failed` notification, which settles criterion 1. If none does, P2-090 fixed the construct, and the pair
  has to be named by running this branch's `CompareCommand` fix on a tree without P2-090.
- Decision: the new rule id IR014 lives in `IrDiagnosticIds.cs`, which the Files list does not name; a validator check
  cannot be reported without an id.
- 2026-10-03, runs on a build of this branch merged with P2-109's (PR #372), at main c11f10f:
  - `duplicati-3124`, `full`: exit 1 (divergent results present), 12,352 s, SARIF written, no
    `toolExecutionNotification`, nothing in `properties.unverified`. By rule: EQ001 5,641, EQ002 32, EQ003 424,
    EQ004 92, EQ005 65, EQ006 184 (6,438 results). So no pair throws at weighing on Duplicati once P2-090 is in.
    Criterion 5 holds for this pair.
  - `openra-17989`, `full`: no SARIF. The run passed the point where ef79ff6 died and verified 1,230 of 10,114
    pairs, then one pair (`OpenRA.WidgetLoader::LoadWidget(OpenRA.Widgets.WidgetArgs,OpenRA.Widgets.Widget,OpenRA.MiniYamlNode)`)
    first sat in the unroller for over ninety minutes (P2-109, fixed) and then, with that fix, overflowed the native
    stack in the trace encoding, which ends the process (P2-113, open). Criterion 5 for OpenRA waits on P2-113.
    A weighing failure is reported when the loop reaches its pair, so the 8,884 pairs after that one say nothing yet
    about whether an OpenRA pair still throws at weighing.
  - One pair-level failure in verifying, outside this ticket and not filed yet:
    `OpenRA.ObjectCreator::.ctor(OpenRA.Manifest,OpenRA.InstalledMods)`, "domain sort ... and parameter sort ... do
    not match" between two tuple array types that differ only in element names.
- Criterion 1 is still open: no run has named the procedure. Duplicati's clean run says its construct is the one
  P2-090 fixed (a branch on a compile-time constant whose dead edge named an unlowered block), by inference, not by
  identity. Naming it takes this branch's `CompareCommand` fix on a tree without P2-090, run on either pair.
- The P2-065 worktree that held both pairs' restored checkouts no longer exists (seen 2026-10-03 after the Duplicati
  run ended), so the next run starts from `corpus.ps1 -Fetch` and a restore of both sides.
- 2026-10-03, criterion 1. A build of 0baa20f (the commit before P2-090) with a throwaway line that prints every
  lowered body holding a block with no terminator, run `--lower-only` on both pairs (exit 0 on each, 73 and 74 seconds):
  - `openra-17989`: one pair, `OpenRA.Server.Program::Main(string[])`, both sides. Of 104 blocks, B61 has no
    terminator and no instructions; the `IrBranch` of a loop header (two `IrPhi`, then the `IrConst` it branches on)
    names it as its false target.
  - `duplicati-3124`: one pair, `Duplicati.UnitTest.TestUtils::GrowingFile(string,System.Threading.CancellationToken)`,
    both sides. Of 30 blocks, B12; an empty block with an `IrGoto`, a copy of the `finally`, names it.
  - The construct is the same on both: a `WhileLoop` whose condition is the `Literal` `true` and that nothing leaves
    normally, so the condition's false edge leads only to code that is never reached. On OpenRA the loop is the last
    statement of a method that returns nothing and holds a second such loop left by a `Branch` (`break`); on Duplicati
    it is inside a `Try` with a `finally`, in an `async` method. This is P2-090's dead edge of a branch on a
    compile-time constant, in a shape its Notes did not list: they say `while (true)` emits no conditional branch,
    which holds only when something after the loop is reachable.
- Criterion 2: `IrLowererTests.AnEndlessLoopWhoseExitIsNeverReachedLowers`, one row per pair's shape. On 0baa20f both
  rows fail with the `NullReferenceException` from `IrValidator.IrChecker.CheckTargets`; here both pass.
- Criterion 3: P2-090 (PR #333) is the lowering fix; nothing in `src/Equiv.Frontend.CSharp` changes here. The validator
  did not accept the bad IR, it threw on it; it now reports IR014 (`IrValidatorTests.IR014ABlockWithoutATerminator`).
- Criterion 5. No pair fails at weighing on either pair any more, so neither `full` run was repeated on this branch's
  head; the two runs below are the evidence, and the only code they lack or differ in is `Verified`'s catch, which
  neither would have entered.
  - `duplicati-3124`: the run above (this branch's fix on c11f10f). Exit 1, SARIF written, by rule EQ001 5,641,
    EQ002 32, EQ003 424, EQ004 92, EQ005 65, EQ006 184.
  - `openra-17989`: P2-121's criterion 4 run at 550e260, which has P2-090 and not this branch. Exit 5, SARIF written
    (10,206 results) in 4,593 seconds, by rule EQ001 9,777, EQ002 6, EQ003 266, EQ004 31, EQ005 62, EQ006 64. Its one
    notification and one `unverified` entry are the `OpenRA.ObjectCreator::.ctor` sort mismatch noted above, a failure
    in verifying. `OpenRA.Server.Program::Main(string[])` is EQ003 in it.
- The pair checkouts used here are in the `pair-weighting-crash-fix-a9ea8b` worktree's `.corpus/`.
