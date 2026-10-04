# P1-034 Spike: does a call trace encoded without sequences and datatypes make the hard queries easier?
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-025

## Goal
P1-025 exported the 142 rung 1 queries Z3 gives up on for `gitextensions-8522`. 139 of them hold
the call trace, which `TraceEncoder` encodes as a sequence of an `Event` datatype with an integer
callee (`docs/runs/2026-10-04-solver-portfolio.md`). That theory combination is the reason Bitwuzla
reads none of the files, and it may be part of why Z3 and cvc5 give up on them: nobody has measured
it. On an unrolled pair the trace has a bounded length and every call site is known, so "the traces
are equal" can be written without sequences: the same number of events, and at each position the
same callee and equal arguments.

Measure whether that encoding decides more. This is a spike. It changes nothing under `src/`.

## Spec references
ADR 0018 (the trace is an observable), ADR 0014, VERIFICATION-MODEL.md section 5,
`src/Equiv.Verify.Z3/TraceEncoder.cs`, `ProductEncoder.cs` (`Encode`, the `equal` list),
`tools/spikes/solver-portfolio/` (the export, the solver runs and the read-back to reuse),
`docs/runs/2026-10-04-solver-portfolio.md`, P2-101 (which features the hard queries share).

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/spikes/trace-encoding/` builds, for each of the 142 queries, the same product with the
   trace encoded positionally in bit-vectors, arrays and uninterpreted functions only, and states in
   its README why the positional form is equivalent to the sequence form on an unrolled pair.
2. A `--self-test` checks on at least six hand-written pairs (equal traces, a different callee, a
   different argument, a different length, a call under a branch on one side only, a reordered pair
   of calls) that the two encodings give the same answer.
3. Each query is asked of Z3 in both encodings at the default `resourceLimit`, and of cvc5 and
   Bitwuzla in the positional one for `timeoutMs`. Per solver and encoding: unsatisfiable,
   satisfiable, unknown, timeout, error. Satisfiable answers are read back as P1-025's were.
4. `docs/runs/<date>-trace-encoding.md` holds the tables, and one line: the number of the 142 the
   positional encoding decides that the sequence encoding does not, per solver, and how many of
   those are proofs. Identities and counts only.
5. If any solver proves at least one pair that is Unknown(timeout) today, write the ticket that
   moves `TraceEncoder` to the positional form on rung 1, and, if Bitwuzla is the one that decides
   the most, a clarification on ADR 0050 adding it as a second `ISmtSolver`. Otherwise one measured
   line in ROADMAP's post-MVP list.

## Files
`tools/spikes/trace-encoding/**`, `docs/runs/<date>-trace-encoding.md`, and either a ticket (and an
ADR 0050 clarification) or `docs/ROADMAP.md`.

## Tests
The `--self-test` of criterion 2.

## Size guard
Any edit under `src/`: stop. If the positional encoding needs the heap or the pure functions
re-encoded as well, stop and record what forced it; the spike changes the trace only.

## Out of scope
Rungs 2 to 5, whose traces carry cut events of loop segments. Adopting Bitwuzla. Constant arrays
(P1-033 handles them for cvc5).

## Notes
- From P1-025. Bitwuzla also rejects an `or` of one argument, which Z3 prints in the `opaque`
  queries; unwrap it as the P1-025 spike unwraps `seq.++`.
