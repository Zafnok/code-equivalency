# P1-024 Trace-guided loop alignment: runs propose a pairing of iterations, relational induction proves it
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-023, P1-009

## Goal
Rung 2 proves loops that run in lockstep, one iteration against one. This ticket generalises its
schedule: `a` legacy and `b` modern iterations first, then every `m` legacy iterations against `n`
modern ones. The schedule is proposed from runs of both sides in `IrInterpreter` and proved by the
same cut-and-induct argument rung 2 uses, so a wrong schedule can only fail to prove (ADR 0036).
Unlike rung 4 it works on loops whose bodies make calls, because paired stretches are compared as
rung 2 compares them: call traces included.

When done, a loop against its unrolled, batched or peeled form is Equivalent with `proofMethod:
aligned-induction`, on the schedule classes P1-023 found.

## Spec references
VERIFICATION-MODEL.md section 5.1 (rungs 2 and 3, header states, what a base model must replay to),
ADR 0008 (the ladder), ADR 0036 decision 1, ADR 0018 (the call trace and positions),
`src/Equiv.Verify.Z3/LockstepInduction.cs`, `KInduction.cs`, `LoopLadder.cs`,
`Ladder/TraceInvariantProposer.cs`, `docs/runs/<date>-loop-alignment-spike.md`.

## Design
Proposal. Reuse P1-023's search, moved into `src/`: run both sides on the proposer's inputs (earlier
counterexamples first), record header visits and the call events between them, and return the
fitting schedules `(a, b, m, n)`, smallest `m + n` first, at most three. No fitting schedule means
the rung does not apply.

Proof, for one schedule. Build from each side a procedure whose loop body is `m` (or `n`) copies of
the original body, with the exit test kept between copies, after `a` (or `b`) peeled copies. `IrUnroller`
already copies bodies; an exit between copies leaves the loop as the original exit does. The two
rewritten procedures have the same loop forest by construction, and rung 2 then runs on them
unchanged: base from equal inputs to the first paired header, step from equal header states to the
next, exits with equal observables. Rung 3's warm-up applies the same way.

Why this is sound. Each rewritten procedure has exactly the runs of its original: copying a body
with its exit tests kept changes no run. So a proof about the rewritten pair is a proof about the
original pair, whatever the schedule. The schedule only decides whether the proof goes through.

Pitfalls.
- Header states pair by name in rung 2. After rewriting, the paired header is the first copy's on
  each side; phis of inner copies are not header state.
- Call positions (ADR 0018) count calls a side made before. A paired stretch must make the same
  number of calls on both sides or the positions drift and nothing after it agrees; the trace search
  already requires equal call events, so a schedule that fits has this.
- A model of a step obligation may start from an unreachable state. Only a base model that replays
  through the original procedures is a counterexample, as in rung 2.
- Nested loops: rewrite one loop pair at a time, outermost first, and only where the forests differ
  at that loop alone. Anything else does not apply.
- `m` and `n` multiply the encoded body. Refuse a rewritten body above the size rung 1 refuses.

## Acceptance criteria (all must hold; nothing beyond them)
1. `equiv-adr`'s bar test is run first and its outcome is in Notes. The expected vehicle is a
   clarification of ADR 0008 (a rung between 3 and 4) under ADR 0036.
2. The ladder runs the new rung after rung 3 on a pair that would otherwise be `unaligned-loop`,
   before rung 4. A proof is Equivalent with `proofMethod: aligned-induction` and
   `properties.alignment` `{ legacyOffset, modernOffset, legacyStep, modernStep }`. Each schedule
   tried is a step of `properties.ladderTrace`.
3. Three new samples are Equivalent by the new rung, each with a loop body that makes a call:
   `loop-unrolled` (1:2), `loop-batched` (m:1 with a remainder loop), `loop-peeled` (offset 1). Each
   has a variant with a changed bound that is Divergent, by rung 1, and never Equivalent.
4. On P1-023's pairs with a fitting schedule other than lockstep, a corpus run through
   `equiv-corpus-run` on `gitextensions-8522` reports how many the rung proves. The number and the
   run are in Notes.
5. VERIFICATION-MODEL.md section 5.1's table has the rung, and section 7's ladder properties cover
   it: section 7's soundness harness and ladder monotonicity run against it independently.
6. A pair no schedule fits keeps the verdict and the `ladderTrace` it has today, plus one step.

## Files
`src/Equiv.Verify.Z3/Ladder/AlignmentProposer.cs`, `src/Equiv.Verify.Z3/AlignedInduction.cs`,
`src/Equiv.Verify.Z3/LoopLadder.cs`, `src/Equiv.Core/` (`ProofMethod`, the SARIF property), their
tests, `samples/loop-unrolled/**`, `samples/loop-batched/**`, `samples/loop-peeled/**`,
`docs/VERIFICATION-MODEL.md`, `docs/adr/0008-*.md` (clarification only), `README.md` (the ladder
sentence).

## Tests
`Proposer_FindsTheUnrollingSchedule`, `Proposer_FindsAPeeledSchedule`,
`Proposer_ReturnsNothingWhenCallEventsNeverPair`, `Rewrite_KeepsEveryRunOfTheOriginal` (property:
the rewritten procedure and the original agree in `IrInterpreter` on generated inputs),
`AlignedInduction_ProvesTheThreeSamples` (snapshots), `AlignedInduction_NeverProvesAMutant` (section
7's harness on this rung), `WrongScheduleIsNeverAProof` (property: a random schedule on a random
non-equivalent pair is never Equivalent), `LadderTrace_ListsEachScheduleTried`.

## Size guard
A schedule shape beyond `(a, b, m, n)`, data-dependent alignment (pair iterations by a predicate on
the state), or more than three schedules tried per pair: stop and file a ticket.

## Out of scope
Loops against a callee (LINQ, P2-096). Loop fusion and fission (two loops against one): rung 4 owns
them where it applies. Disjunctive invariants. Traces from the real runtimes.

## Notes
- From the 2026-10-03 improvement review (its third priority). Gated by P1-023 criterion 4: fewer
  than 10 alignable pairs closes this ticket unbuilt.
