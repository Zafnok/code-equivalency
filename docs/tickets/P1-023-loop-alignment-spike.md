# P1-023 Spike: how many `unaligned-loop` Unknowns have an alignment that their runs show?
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-009

## Goal
Rungs 2 and 3 pair the n-th iteration of one side with the n-th of the other. A loop that was
unrolled, batched, peeled or had its exit test moved does not run in lockstep, and the pair ends
Unknown with reason `unaligned-loop`: 89 results on the three large runs. Rung 4 does not help on
real code, because it does not apply to a pair where either side calls or applies a pure function.
Semantic program alignment (Churchill, Padon, Sharma and Aiken, PLDI 2019) and property-directed
self-composition (Shemer et al., CAV 2019) find the pairing from runs of the two programs and then
prove it. Citations are from memory; check them before relying on them.

Before anyone builds that, measure on the 89 how many have a pairing that runs reveal, and what
stops the rest. 89 is 4.0% of the 2,246 changed pairs, so this technique cannot clear ADR 0028's bar
on unknown count alone; the spike says whether the alignable part is large enough to build P1-024.

## Spec references
VERIFICATION-MODEL.md section 5.1, `src/Equiv.Verify.Z3/LockstepInduction.cs` (`Misalignment`),
`src/Equiv.Verify.Z3/Ladder/TraceInvariantProposer.cs` (running both sides segment by segment),
ADR 0036, the P1-011 and P1-019 spikes (the shape to follow).

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/spikes/loop-alignment/` reads the three large runs' SARIF, and for each `unaligned-loop`
   Unknown lowers the pair and records `LockstepInduction`'s misalignment text, the loop forests of
   both sides, and whether a loop body holds a call, an `IrPure` or an opaque.
2. For each pair it runs both sides in `IrInterpreter` on up to 200 inputs, as
   `TraceInvariantProposer` does, and records per run each header visit and the call events between
   visits. It then searches schedules "after `a` legacy and `b` modern iterations, every `m` legacy
   iterations pair with `n` modern ones", for `a`, `b` in 0..2 and `m`, `n` in 1..4. A schedule fits
   when, on every run, the call events of each paired stretch are equal and both sides leave their
   loops at a paired point.
3. `docs/runs/<date>-loop-alignment-spike.md` reports: pairs with a fitting schedule, by schedule;
   pairs with none, by cause (different number of loops, a loop on one side and a callee on the
   other such as LINQ, an opaque in the loop, no run that reaches the loop, other); and the share of
   each over the 89 and over the changed pairs. Identities, schedules and causes only.
4. The line that decides P1-024: the number of pairs with a fitting schedule other than 1:1 with no
   offset. If it is under 10, P1-024 is closed with a measured line in ROADMAP's post-MVP list.
5. Nothing under `src/` changes. The spike is not in `Equiv.slnx`.

## Files
`tools/spikes/loop-alignment/**`, `docs/runs/<date>-loop-alignment-spike.md`, `docs/ROADMAP.md`,
`docs/tickets/P1-024-*.md` (its Notes, or its closure).

## Tests
A `--self-test` over two hand-written IR pairs: a loop against its 2:1 unrolling, and a loop with one
peeled iteration.

## Size guard
Proving anything with Z3 is P1-024. A schedule search beyond the bounds of criterion 2, or more than
about 800 lines: stop.

## Out of scope
Traces from the real runtimes: `IrInterpreter` runs are enough to propose. A loop against
`Where(...).ToList()` (P2-096 owns it; count it under its cause).

## Notes
- From the 2026-10-03 improvement review (its third priority). The review expects non-lockstep loops
  to be the most common structural change; the three large runs put `unaligned-loop` fourth among
  Unknown reasons, behind opaque (733), solver budget (400) and abstraction (246).
