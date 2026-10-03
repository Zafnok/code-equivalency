# P2-109 Unrolling a loop costs time in proportion to its size, not to its square
Status: in-progress
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-076

## Goal
In P2-082's `full` run of `openra-17989` (2026-10-03), one pair,
`OpenRA.WidgetLoader::LoadWidget(OpenRA.Widgets.WidgetArgs,OpenRA.Widgets.Widget,OpenRA.MiniYamlNode)`, sat in rung 1
for more than twenty minutes before its first solver query. Its last stage line was `stage=shape`; no `stage=unroll`
line followed. Four stack samples of the running process (`dotnet-stack report`), minutes apart, all show the same
frames: `LoopLadder.Bounded`, `IrUnroller.Unroll`, `IrEditor.CopyLoop`, `IrLoopCopies.Versions`, and under it
`IrCall.Definitions` or `IrPure.Definitions`.

`IrLoopCopies.Versions` finds, for every variable the loop defines, the block that defines it by scanning the loop's
blocks and asking every instruction for a fresh `Definitions()` list. That is the loop's instructions squared, with an
allocation per step, on every `CopyLoop`. P2-076 measured `unroll` and `encode` together at 19 seconds over the whole
Git Extensions verify phase, so this did not show there. It shows on a procedure whose loop holds a call to itself:
`Unroll` inlines the self-call `bound` deep, which nests the loop inside itself, and then copies every loop `bound`
times from the innermost out, so the outer loop's body is many times the source's size by the time `Versions` scans it.

Make the unroller's cost linear in the size of what it copies. This is P2-076's rule again: work that is not a solver
query is made cheaper, never skipped, capped or abandoned.

## Spec references
`src/Equiv.Core/Ir/IrUnroller.cs` (`Unroll`, `IrEditor.CopyLoop`, `IrLoopCopies.Versions`, `IrSsaRepair`),
ticket P2-076 (the stage log, and the rule that no pair is ended early), VERIFICATION-MODEL.md section 5 (rung 1).

## Acceptance criteria (all must hold; nothing beyond them)
1. Measure first. A test or a scratch run unrolls a procedure written for the test (a loop that calls its own
   procedure, and a second loop beside it) at bound 3, and `## Notes` records the instruction count after inlining,
   after unrolling, and the seconds spent in `Versions` and in the rest of `Unroll`. If `Versions` is not the
   dominant cost, say what is, and fix that instead.
2. `IrLoopCopies.Versions` (or whatever criterion 1 names) visits each instruction of the loop a constant number of
   times. `Unroll`, `UnrollInPlace` and `Peel` return the same procedure as before, instruction for instruction, on
   every fixture in `tests/Equiv.Core.Tests/Ir/IrUnrollerTests.cs` and on the property test's generated procedures.
3. A test unrolls a generated loop of n and of 4n instructions and asserts on a count of work done (instructions
   visited, through a counter the test can read), not on wall-clock time: the count grows by about 4, not 16.
4. Rerun `full` on `openra-17989` (`equiv-corpus-run`). `## Notes` records the pair's `stage=unroll` seconds, its
   outcome, and the run's five slowest pairs with their stage totals. If another stage of this pair now takes
   minutes outside a solver query, file it; do not fix it here.

## Files
`src/Equiv.Core/Ir/IrUnroller.cs`, `tests/Equiv.Core.Tests/Ir/IrUnrollerTests.cs`.

## Tests
Named in criteria 2 and 3.

## Size guard
If the unrolled procedure itself is what is too large (the encoder or the solver then spends the time), stop after
criterion 2 and record the sizes: how far a self-call inside a loop is inlined is a question for `equiv-adr`, not
for this ticket.

## Out of scope
Any cap on a rung, a pair or a run. Changing `bound`, how deep a self-call is inlined, or which loops are copied.
Verifying pairs in parallel (P2-077).

## Notes
- Found by P2-082's run, at 630b63d's fix rebased onto 8e0ed3c. The process was sampled while it ran and was not
  stopped. At the time of writing the pair had not finished, so its outcome and its total are not known.
- Not measured: the size of the unrolled procedure. The quadratic scan is read from the code and the stacks; that the
  self-call is what makes the loop large is inferred from `Unroll`'s order of work, not observed. Criterion 1 settles both.
- Criterion 1, measured 2026-10-03 on a procedure written for it (`IrUnrollerTests.LoopAroundASelfCall`: a counting
  loop whose body makes k calls, each ending its block, then calls its own procedure, and a second loop beside it),
  at bound 3, Release build. Instructions before inlining, after unrolling, blocks after unrolling, seconds in `Unroll`:

  | k | instructions in | instructions out | blocks out | before | block index only | after |
  |---|---|---|---|---|---|---|
  | 40 | 50 | 3,650 | 3,185 | 0.46 | 0.13 | 0.05 |
  | 80 | 90 | 6,290 | 5,825 | 2.73 | 0.66 | 0.18 |
  | 160 | 170 | 11,570 | 11,105 | 16.63 | 1.07 | 0.33 |
  | 320 | 330 | 22,130 | 21,665 | 123.10 | 3.67 | 0.69 |

  Doubling the body multiplied the time by six to seven: the cost was cubic, not quadratic as the Goal says.
- Decision: the dominant cost -> `IrEditor.Get` and `IrEditor.Replace`, which found a block by scanning the block
  list. `Versions` called `Get` once per loop block per defined variable, which is where the stack samples sat; with
  the lookup indexed it is 3.67 s of the 123.10, and its own scan the rest. Both are fixed: the editor keeps each
  block's position by id, and `IrLoopCopies` records each variable's defining block as it collects the definitions.
  Alternatives: fixing `Versions` alone, which leaves `IrSsaRepair.Run`'s `Replace` of every block quadratic. Rule:
  measured, criterion 1.
- A first procedure, with the same instruction counts in a handful of large blocks, unrolled in 0.13 s before the
  fix. The cost follows the number of blocks, so a body of calls (each call ends its block) is what shows it.
- Decision: how criterion 3 is tested -> by the ratio of two timings, the fastest of three runs each, for k = 80 and
  k = 320: under ten times, where it was 45 before and is about 4 after
  (`IrUnrollerTests.UnrollingALoopThatCallsItsOwnProcedureIsLinearInItsSize`). The criterion asks for a counter of
  instructions visited. The time was in block lookups that are now dictionary reads, so a counter would count
  whatever the code chose to count and would not notice a scan coming back. Alternatives: a counter on the editor
  read through an internal overload; an absolute time limit. Rule: 3.
- Criterion 2: the existing fixtures, the two snapshots and the property tests in `IrUnrollerTests` pass unchanged
  (797 of 797 in `tests/Equiv.Core.Tests`). A whole-procedure pass per copied loop remains (`IrSsaRepair`'s
  predecessor map, `IrLoopAnalysis.Of` in `Unroll`'s loop), so the cost is blocks times loops, not strictly linear.
- Criterion 4, `full` run of `openra-17989` on 2026-10-03, on a build with this branch and P2-082's branch merged
  (P2-082's fix is what lets the run reach verify at all). The pair's `stage=unroll` took 116.9 s, where before it
  had not finished after more than ninety minutes. The run then died in the next stage: `ProductEncoder.Encode`,
  `TraceEncoder.Trace`, native `Z3_mk_seq_concat`, stack overflow, exit -1073741571, no SARIF. So the pair has no
  outcome and the run has no slowest-five list. The size guard trips: the unrolled procedure is what is too large.
  Its size was not measured (117 s at the linear rate above suggests millions of instructions; that is an estimate).
  Filed as P2-113, which measures it and takes the question to `equiv-adr`.
- The run got to verify 1,230 of 10,114 before the overflow, with one pair-level failure unrelated to this ticket
  (`OpenRA.ObjectCreator::.ctor(OpenRA.Manifest,OpenRA.InstalledMods)`, a sort mismatch in verifying).
