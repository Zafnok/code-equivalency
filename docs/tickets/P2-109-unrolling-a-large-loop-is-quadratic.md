# P2-109 Unrolling a loop costs time in proportion to its size, not to its square
Status: todo
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
