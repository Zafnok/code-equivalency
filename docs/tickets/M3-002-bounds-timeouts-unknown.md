# M3-002 Loop ladder rungs 1 to 3, timeouts, Unknown
Status: done (PR #121)
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
VERIFICATION-MODEL.md sections 1, 5, 5.1, 7; ADR 0008; ADR 0018; M3-001 design (reuse
`ProductEncoder` on IR fragments; do not write a second encoder).

## Design

Loop structure (`IrLoopAnalysis`, in `Equiv.Core.Ir`): back edges from a DFS of the
CFG; natural loop per back edge (header, body blocks, exit edges, latch); loop nesting
forest. The Roslyn CFG is reducible, so every loop has a single header. The loop state is
(a) the header's phis, (b) every SSA value defined outside the loop and used inside it
(loop-invariant live-ins such as `n` in `var n = a.Length; for (...; i < n; ...)`: in SSA these
are not phis, but the body reads them), and (c) the call-position counter `cnt` of M3-001
(ADR 0018). Heap maps that are live at the header are phis or live-ins like any other value.

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
  header state as a tuple of out-params). Paths that leave the procedure before reaching
  the header (early return, throw) stay in the fragment as ordinary exits.
- body: one iteration from the header. It returns how the iteration ended, as an exit
  kind: `continue` (back edge, with the next header state), `leave(e)` (a loop exit edge
  `e`, taken from the header's guard *or* from a `break` or `goto` inside the body, with
  the state at that edge), `return` or `throw` (leaving the procedure from inside the
  loop, with its observables). Iteration trace events are part of the result.
- suffix: one fragment per loop exit edge, from that edge to the procedure's exits.
  Exit edges are paired across sides by their position in the loop's reverse-postorder
  exit list; unequal exit counts mean the loop does not align.

Coupling: header states are related by pairing variables by `SourceName` when both
sides have one, else by position among the header phis; unpaired variables on either
side mean the loop does not align (fall to Unknown(unaligned-loop), rung 4 later).
Three obligations, each a product-program query on fragments:

1. Base: equal inputs imply that both sides either reach the header with equal header
   states and equal prefix traces, or both leave before it with equal observables
   (prefix_old vs prefix_new).
2. Step: equal header states imply the same exit kind on both sides and, per kind: equal
   next states (`continue`); the same paired exit edge with equal states at it (`leave`);
   equal observables (`return`, `throw`); and in every case equal iteration traces
   (body_old vs body_new).
3. Exit: for each paired exit edge, equal states at the edge imply equal observables
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
3. The five fixtures under Tests produce the named verdict path, and so do two more:
   `loop-break-return` (an aligned loop with a `break` and a `return` inside the body,
   unchanged on both sides) and `loop-invariant-livein` (an aligned loop whose guard
   reads a value computed before the loop that is not a header phi). Both are
   `lockstep-induction` Equivalent, and each has a one-line mutation that is Divergent.
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
  at the header, the loop-invariant live-ins and `cnt`, not only the scalar phis.
- `break`, `return` inside the loop and the enumerator `foreach` (M3-010) all give loops
  several exits; do not assume the header's guard is the only way out.
- Traces inside loops: rung 1 compares whole traces; rungs 2 and 3 compare one
  iteration's trace fragment per obligation. Do not try to compare unbounded traces.
- Do not attempt loop invariant synthesis here; that is rung 4 (P1-001).

## Out of scope
Rungs 4 and 5. Termination. Mutual recursion.

## Notes
- Decision: rung 1's "UNSAT gives Equivalent with `boundedBy: k`" is a proof only when a third query shows no input reaches the unrolling bound; otherwise the rung is `inconclusive` in the ladder trace and rungs 2 and 3 run, and a looping pair none of them proves is Unknown. Alternatives: report the bounded Equivalent when rungs 2 and 3 fail (it passes CI for a mutant that differs only after k iterations, which section 7's "never Equivalent" forbids, and it contradicts `loop-to-linq` being Unknown). Rule: 3. VERIFICATION-MODEL section 5.1 patched.
- Decision: an acyclic pair is `Equivalent(bounded)` with no `boundedBy` (criterion 2: "and a loop existed"); its ladder trace is one `bounded: proved` step. Rule: 1.
- Decision: rung 2 cuts each procedure at every loop header at once (`IrFragmenter.Segment`: one acyclic segment from the entry and one per header, each running to the next header it reaches or to an exit of the procedure) instead of a prefix, body and per-exit-edge suffix per loop with loop-summary functions for nested loops. For one loop this is the Design's rule with the exit obligation folded into the step (a segment continues past the loop exit), which needs strictly less (never equal states at the exit edge); sequential and nested loops need no summaries, because every header is a cut point. Soundness is induction on the number of headers reached. Alternatives: the Design's per-loop fragments (the prefix of a second loop contains the first, and a summary needs one uninterpreted function per output). Rule: 4. Flagged in the PR.
- Decision: a segment reports reaching header `i` as a call event `equiv:cut:<i>` whose arguments are that header's state on the edge taken, followed by `throw "equiv:cut"`; `ProductEncoder` then compares the exit kind and state as trace and outcome, so no second encoder and no out-parameter tuples are needed. Rule: 1.
- Decision: the call counter `cnt` is not a fragment input. Each obligation numbers its calls from 0, its call functions are universally quantified per obligation, and every obligation compares traces, so both sides reach every cut point having made the same number of calls (ADR 0018's argument applies per segment). Rule: 4.
- Decision: a header's state is its phis, then every other variable live on entry (parameters in declaration order, then definition order): loop-invariant live-ins, heap maps, and values read after the loop. Coupling pairs phis with phis by unambiguous `SourceName` (same type), else by position; live-ins by shared parameter (ADR 0021), then source name, then position; anything left unpaired, or paired by position with another type, does not align (as the Design says). Loops pair by pre-order of the nesting forest, siblings by header reverse postorder. Rule: 1.
- Decision: rung 3 runs when rung 2 failed on a step obligation (not its base) and each side has exactly one loop. The step unrolls the loop k + 1 times in place (`IrUnroller.UnrollInPlace`) and assumes both sides reach the next k header copies with equal coupled phis; the base peels k iterations (`IrUnroller.Peel`) and checks the peeled headers' phis as well as the segment, so the first k + 1 arrivals agree. Rule: 4.
- Decision: rung 1 inlines self-calls k deep only when that is exact: every parameter `In`, no `IrMapWrite`, no `array.`/`length.` input, every self-call has a threw flag and a receiver-plus-arguments shape; otherwise rung 1 is `not-applicable`. A callee's `throw` becomes `threw = true` with a default result. Rung 2 keeps the self-call as the shared call (section 5.1's mutual summary), so it can prove recursion unbounded, and its models are never replayed (the call's answer is not the procedure's). Rule: 4.
- Decision: mutual recursion is not visible to a pair-wise backend (a call to another matched procedure is a shared function, ADR 0019; an IR fixture has one procedure per side), so criterion 3's mutual-recursion fixture cannot give Unknown(recursion) without a Core contract change. ADR 0030 is proposed (PR #120) (modular reading is sound by the mutual-summary rule); `recursion-unaligned` (self-recursion no rung decides) is the Unknown(Recursion) fixture meanwhile. Rule: ADR.
- Decision: the Unknown reason of an undecided looping pair is `Opaque` if a failed obligation's model reaches an opaque, else `Recursion` if a side calls itself, else `UnalignedLoop` (not aligned, or aligned and no induction proved it), else `Timeout`; the detail is that rung's detail. `UnknownReason.Loop` is gone (criterion 1). Rule: 1.
- Decision: `Equivalent(ProofMethod Method, int? BoundedBy = null)`, a `ProofMethod` enum, and `Verdict.Ladder` (an `ImmutableArray<LadderStep>` init property on the base record, compared structurally) carry the new properties; `SarifReportWriter` writes `properties.proofMethod`, `boundedBy`, `unknownReason` and `ladderTrace` (a list of `{rung, outcome, detail}`), because criterion 2 names `properties.*`. Alternatives: leave SARIF to M3-003 (whose size guard allows only composition). Rule: 1.
- Decision: `IrUnreachable` is no longer an assertion of `ProductEncoder`; each side's `Unreachable` term is assumed false by every query except rung 1's bound query. The 13 encoder snapshots gain two `unreachable.*` lines and `unreachable` loses its `(not old.reach.B1)` line. `ProductEncoder.Analyze` is replaced by `IrLoopAnalysis`. Rule: 1.
- Decision: copy names are `name$c` (IR names cannot contain the Design's `@`), inlined copies `name$r<n>`, repair phis `name$phi`; a taken name gets more `$`. `IrLoop` is its own file (MA0048; CA1034 forbids a public nested type), so `Equiv.Core.Ir` gains four files, not three. Rule: 4.
- Decision: irreducible control flow (never produced by the Roslyn CFG) is `Unknown(UnalignedLoop)`; `IrUnroller` rejects it, since a natural loop there can contain the entry. Rule: 4.
- Decision: `samples/identical` and `samples/renamed-locals` had no loop, so criterion 4 was vacuous; both gain `Calculator.SumTo(int)` (renamed locals on the renamed side), and their READMEs list it. Rule: 3.
- Decision: a base model replays through the original procedures with a 100,000-step budget; a replay that runs out or reaches an opaque is not a counterexample. Rule: 4.
- Decision: in debug builds `IrUnroller` and `IrFragmenter` `Debug.Assert` that valid input gave valid output; `LoopTransformPropertyTests` and the unit tests assert it explicitly (criterion 6). Rule: 3.
- Note: Z3's base-obligation models pick large loop bounds, so a base counterexample on a loop bounded by an input rarely finishes replay within the budget (the pair stays Unknown, which is sound). Rung 1 is where refutations come from in practice; `late-divergence` uses a ten-iteration loop to show a rung 3 refutation.
- Note: the strict coupling rule is brittle: a constant or input read in the loop on one side only (e.g. hoisted out of the loop, or no longer used) makes the loops unaligned. Treating an unpaired live-in as an unconstrained one-sided input would be sound and more precise; left to a follow-up, not done here.
- Note: blocks that can only reach an unrolling cut sit outside the natural loop, so the copies share them (see `IrUnrollerTests.NestedLoopsUnrollInsideOut.verified.txt`); the result is valid SSA and exact.
- Note: on the generated looping pairs, `Verify(P, P)` was proved by rung 1 (exhaustive bound) about 3 times in 4 and by rung 2 otherwise; mutants were refuted by rungs 1, 2 and 3 and otherwise Unknown(Opaque) or Unknown(UnalignedLoop).
