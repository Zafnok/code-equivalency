# P2-121 A pair whose unrolled body is huge ends the run with a native stack overflow
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-109

## Goal
With P2-109's fix, the `full` run of `openra-17989` (2026-10-03) gets
`OpenRA.WidgetLoader::LoadWidget(OpenRA.Widgets.WidgetArgs,OpenRA.Widgets.Widget,OpenRA.MiniYamlNode)` through
`stage=unroll` in 116.9 seconds, and then the process dies: exit -1073741571 (0xC00000FD, stack overflow), no SARIF,
8,884 pairs never looked at. The stack is `LoopLadder.Bounded`, `ProductEncoder.Encode`, `FragmentEncoder..ctor`,
`TraceEncoder.Trace`, and the native `Z3_mk_seq_concat`.

Two things are wrong.
1. A stack overflow cannot be caught, so ADR 0023's "a crash on one pair does not end the run" does not hold for it.
   The per-pair `try` in `CompareCommand.Verified` never sees it.
2. The unrolled procedure is far larger than its source. The loop of this procedure calls the procedure itself;
   `IrUnroller.Unroll` inlines the self-call `bound` deep, which nests the loop in itself, and then copies every loop
   `bound` times from the innermost out. At the now linear rate P2-109 measured (22,000 instructions in 0.7 s), 117
   seconds of unrolling is millions of instructions. That estimate is not a measurement.

## Spec references
ADR 0023 (a crashing pair does not end the run), ADR 0029 decision 5 (no run-level budget produces Unknowns),
VERIFICATION-MODEL.md section 5 (rung 1, the bound, self-call inlining), `src/Equiv.Core/Ir/IrUnroller.cs` (`Unroll`,
`Inline`), `src/Equiv.Verify.Z3/TraceEncoder.cs` (`Trace`), `src/Equiv.Verify.Z3/LoopLadder.cs` (`Bounded`),
tickets P2-076 and P2-109.

## Acceptance criteria (all must hold; nothing beyond them)
1. Measure. Record in `## Notes`, for the pair above (identity and counts only): blocks and instructions as lowered,
   after `Inline`, and after `Unroll`, per side; the number of self-call sites and of loops; and the number of
   operands `TraceEncoder.Trace` hands `Z3_mk_seq_concat`.
2. A test in `tests/Equiv.Verify.Z3.Tests` verifies a pair written for the test that reaches the same overflow, or
   `## Notes` says why no pair of a size a test can afford does, and the test covers the largest that one can.
3. The run survives the pair. Whatever makes that true is decided through `equiv-adr`, because each candidate changes
   a recorded decision: building the trace so no native call recurses on its length; verifying on a thread with a
   larger stack; or an Unknown with its own reason for a pair whose unrolled size passes a stated limit, which is a new
   way to end a pair and so needs ADR 0029 and P2-076's rule weighed against it.
4. A `full` run of `openra-17989` writes its SARIF. `## Notes` records the pair's outcome and stage seconds.

## Files
Decided by criterion 3's outcome; `docs/adr/` if the bar test says so.

## Tests
Named in criterion 2.

## Size guard
If criterion 1 shows the size comes from something other than self-call inlining times unrolling, stop and rewrite
the Goal before fixing anything.

## Out of scope
The value of `bound`. Verifying pairs in parallel (P2-077). The `ObjectCreator::.ctor` sort mismatch in the same run.

## Notes
- Found by P2-109's criterion 4 run, which is also P2-082's criterion 5 run. Before P2-109 the same pair sat in
  `IrUnroller` for over ninety minutes and the run was stopped by hand, so the overflow was never reached.
