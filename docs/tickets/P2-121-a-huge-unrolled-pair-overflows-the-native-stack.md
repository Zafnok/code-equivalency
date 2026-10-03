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
- Criterion 1, measured 2026-10-03 on the pair's two lowered procedures, dumped from a run of `openra-17989` and
  unrolled as rung 1 does (after `ProductEncoder.ShareFragments`, which changes nothing here), Release build. The two
  sides are the same size at every step.

  | per side | blocks | instructions | calls | loops |
  |---|---|---|---|---|
  | as lowered | 98 | 155 | 32 | 3 |
  | after `Inline`, bound 3 | 394 | 652 | 124 | 11 |
  | after `Unroll`, bound 3 | 60,589 | 134,299 | 19,484 | 0 |

  One self-call site. Three loops, of 11, 18 and 5 blocks; the third is nested in the second, and the self-call is in
  it, so in both. At bound 1 `Unroll` gives 201 blocks and at bound 2 1,633. `Unroll` took 43 to 47 seconds a side.
  `TraceEncoder.Trace` handed `Z3_mk_seq_concat` 19,485 operands a side: one per block that calls, and the empty
  sequence.
- The size guard does not trip: the size is self-call inlining (98 blocks to 394, 3 loops to 11) times unrolling (394
  to 60,589). The Goal's "millions of instructions" was wrong by a factor of ten or more. The unroller's time is blocks
  times loops (P2-109's Notes), so 117 seconds is not 22,000 instructions in 0.7 seconds scaled up.
- What overflows, measured in a program that only builds the term (Z3 5.1.0, 1 MB stack): `Z3_mk_seq_concat` on 8,000
  operands returns and on 10,000 overflows. A chain of two-operand concatenations 10,000 deep, built one call at a
  time, overflows the same way, so the recursion is on the term's depth, not on the call's operand count. A balanced
  tree of 100,000 operands builds, and a solver with this backend's tactics answers a query over it.
- Decision: how the run survives the pair -> the trace is built so that no native call recurses on its length: a tree
  of concatenations of at most `TraceEncoder.MaxOperands` operands each. Alternatives: verifying on a thread with a
  larger stack, which moves the limit without removing it; an Unknown with its own reason past a size limit, which is
  a new way to end a pair (ADR 0029 decision 5, P2-076) and is not needed while the term can be built. Rule: 3.
- Decision: the vehicle under `equiv-adr`'s bar test -> its first row, a dated Clarification on ADR 0023: the ADR
  already decides that a crash on one pair does not end the run, and a crash no `catch` sees is a case it did not
  spell out. No verdict, reason, rule id or SARIF shape changes, so no new ADR. Alternatives: a new ADR. Rule: the
  bar test.
- Decision: `MaxOperands` -> 256, about a thirtieth of the depth that overflowed, and far above any trace in the
  fixtures, so every term a test pins is the same as before (`LadderFixtureTests.AssertionsAreUnchanged` and the
  `EncoderSnapshotTests` snapshots pass unchanged). A block's own events go through the same function. Alternatives:
  a balanced binary tree for every trace, which changes every pinned term for no gain. Rule: 1.
- Criterion 2: `Z3BackendTests.APairWhoseUnrolledTraceIsThousandsOfBlocksLongIsVerified` verifies a loop that calls
  its own procedure, 13,200 blocks that call a side once unrolled. With `MaxOperands` raised to 1,000,000 the same
  pair ends the test host with the ticket's stack (`LoopLadder.Bounded`, `ProductEncoder.Encode`,
  `FragmentEncoder..ctor`, `TraceEncoder.Trace`, `Z3_mk_seq_concat`). The test sets `ResourceLimit` to 200,000, so
  rung 1 gives up at once and rung 2 proves the pair, in about 3.5 seconds. `TraceEncoderTests` has two more, on the
  term alone: 20,000 blocks, and one block of 20,000 events, each in order and no deeper than 264.
- Seen while sizing that test, not fixed here: at the default resource limit and a 10 second timeout, the same pair's
  `check:bound` query returned unknown after 84 seconds, past both the timeout and P2-076's interrupt at four times
  it.
