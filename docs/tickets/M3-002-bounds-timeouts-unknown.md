# M3-002 Loop ladder rungs 1 to 3, timeouts, Unknown
Status: todo
Effort: L
Model: Opus, high effort (max if available). This is the hardest MVP ticket. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-001, M2-004

## Goal
Procedures with loops or self-recursion are verified by rungs 1 to 3 of
VERIFICATION-MODEL.md section 5.1 on top of the M3-001 encoder: bounded unrolling,
lockstep relational induction (unbounded Equivalent for aligned loops), k-induction.
Every verdict carries `proofMethod`; bounded ones carry `boundedBy`. Unknown reasons
are precise.

## Spec references
VERIFICATION-MODEL.md sections 1, 5, 5.1, 7; ADR 0008; M3-001 design (reuse
`ProductEncoder` on IR fragments; do not write a second encoder).

## Design

Loop structure (`IrLoopAnalysis`, in `Equiv.Core.Ir`): back edges from a DFS of the
CFG; natural loop per back edge (header, body blocks, exit edges, latch); loop nesting
forest. The Roslyn CFG is reducible, so every loop has a single header. Live variables
at the header are the phis of the header; those are the loop state.

**Rung 1, bounded unrolling** (`IrUnroller.Unroll(proc, k)` -> acyclic `IrProcedure`):
clone the loop body k times with fresh SSA names (suffix `@i`), rewrite the header phis
of copy i to take the latch values of copy i-1, and replace the back edge of the last
copy with a goto to a block whose terminator is `IrUnreachable`. Nested loops are
unrolled inside out. Validate the result. Encode the pair with `ProductEncoder`:
SAT gives a real trace (unrolling under-approximates, so refutations are sound):
Divergent. UNSAT gives Equivalent with `boundedBy: k` if either side had a loop.
Self-recursion is treated as a loop with depth k by inlining; mutual recursion is
Unknown(recursion) in this ticket.

**Rung 2, lockstep relational induction.** Only for pairs where the loops align:
same number of loops, same nesting forest shape, and pairing by pre-order in the
forest. For an aligned pair (L_old, L_new) at the same nesting level, split each
procedure into three acyclic fragments, each an `IrProcedure` whose parameters are the
live-in variables of the fragment:

- prefix: entry to header (the loop is cut at the header; the fragment returns the
  header state as a tuple of out-params),
- body: header to latch, with the guard evaluated first; returns the next header state
  plus the guard value and whether the body threw,
- suffix: exit edges to return.

Coupling: header states are related by pairing variables by `SourceName` when both
sides have one, else by position among the header phis; unpaired variables on either
side mean the loop does not align (fall to Unknown(unaligned-loop), rung 4 later).
Three obligations, each a product-program query on fragments:

1. Base: equal inputs imply equal header states (prefix_old vs prefix_new).
2. Step: equal header states and both guards true imply equal next states, equal
   guards, and equal threw flags and traces for that iteration (body_old vs body_new).
   Also: equal header states imply equal guards (so the loops exit together).
3. Exit: equal header states with both guards false imply equal observables
   (suffix_old vs suffix_new).

All three UNSAT: unbounded Equivalent, `proofMethod: lockstep-induction`. A SAT in the
base or exit obligation is a real counterexample only after replay through the
interpreter from real inputs; a SAT in the step obligation is *not* a counterexample
(the assumed state may be unreachable): record it and fall through to rung 3.

Nested loops: an inner aligned pair is proved first and then replaced in the outer body
by an uninterpreted "loop summary" function pair shared by both sides (same identity,
because they were just proved equivalent). If any inner pair fails, the outer is
Unknown(unaligned-loop).

**Rung 3, k-induction.** Same as the step obligation but assume k consecutive
iterations agreed (unroll the body k times inside the fragment, assert equality after
each of the first k, check the k+1th). k is the same option as rung 1's bound.

**Driver** (`LoopLadder`): rung 1 first always (cheap, refutes); then rung 2 if aligned;
then rung 3; else Unknown(unaligned-loop). Unknown reasons: `timeout`, `opaque`,
`unaligned-loop`, `recursion`, `unmatched-overload` (from matching). Each carries a
human-readable detail. SARIF properties: `proofMethod`, `boundedBy`, `unknownReason`,
`ladderTrace` (which rungs ran and their outcome), for M3-003 to emit.

## Deliverables
- [ ] `IrLoopAnalysis`, `IrUnroller`, `IrFragmenter` in `Equiv.Core.Ir`, each unit-tested
      on hand-written IR text fixtures (single loop, nested loops, loop with early return,
      loop that throws).
- [ ] `LoopLadder`, `LockstepInduction`, `KInduction` in `Equiv.Verify.Z3`.
- [ ] Fixtures for each verdict path: aligned unchanged loop (rung 2 Equivalent); loop
      bound changed (rung 1 Divergent, replayed); loop body needs warm-up (rung 3);
      loop-to-LINQ rewrite (Unknown(unaligned-loop)); mutual recursion (Unknown(recursion)).
- [ ] Property tests: soundness harness from M3-001 extended with looping generators;
      ladder monotonicity (a pair proved on rung n is not refuted on rung m; every
      Divergent replays).
- [ ] Sample `loop-bound-change` end to end produces Divergent; sample `identical`
      loops produce unbounded Equivalent.

## Acceptance criteria (all must hold; nothing beyond them)
1. `Verify` on a pair with loops never returns `Unknown(Loop)` any more; it returns one
   of: Divergent (rung 1, replayed), Equivalent with `proofMethod` in
   {`bounded`, `lockstep-induction`, `k-induction`}, or Unknown with reason in
   {`Timeout`, `Opaque`, `UnalignedLoop`, `Recursion`}.
2. `properties.boundedBy` is present exactly when `proofMethod == bounded` and a loop
   existed; `properties.ladderTrace` lists every rung attempted with its outcome.
3. The five fixtures under Tests produce the named verdict path.
4. `samples/identical` and `samples/renamed-locals` loops are `lockstep-induction`
   Equivalent (unbounded); `samples/loop-bound-change` is Divergent on rung 1.
5. Soundness and monotonicity properties pass 200 cases each with looping generators.
6. `IrValidator` reports zero diagnostics on every unrolled and fragmented procedure
   produced during the test runs (asserted inside the tests).

## Size guard
Three files in `Equiv.Core.Ir` (analysis, unroller, fragmenter) and three in
`Equiv.Verify.Z3` (ladder, lockstep, k-induction). Nested loops beyond one level of
alignment and mutual recursion are Unknown, not code.

## Pitfalls
- Unrolling and fragmenting must produce valid SSA; run `IrValidator` on every
  intermediate procedure in tests and in debug builds.
- The step obligation's assumed state must include the heap map variables that are live
  at the header, not only scalars.
- Traces inside loops: rung 1 compares whole traces; rungs 2 and 3 compare one
  iteration's trace fragment per obligation. Do not try to compare unbounded traces.
- Do not attempt loop invariant synthesis here; that is rung 4 (P1-001).

## Out of scope
Rungs 4 and 5. Termination. Mutual recursion.

## Notes
