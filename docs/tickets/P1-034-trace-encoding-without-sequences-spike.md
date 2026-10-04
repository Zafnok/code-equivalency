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
- Result (2026-10-04, `docs/runs/2026-10-04-trace-encoding.md`): all 142 products build in the
  positional form (logic `QF_AUFBV`). Z3 answers 72 of them where it answered none: 4 unsatisfiable,
  68 satisfiable (26 replay to a Divergent, 42 to Unknown(abstraction)). cvc5 answers 78 against 56
  in the sequence form in the same pass. Bitwuzla 0.9.1 answers none as the file is written and 103
  with each uninterpreted sort defined as a 64-bit vector. No pair is proved: the four unsatisfiable
  `divergence` queries all have a satisfiable `opaque`.
- Deviation: criterion 5 writes the follow-on ticket only if some solver proves a pair, and none
  does. P1-038 is written all the same: Z3 alone decides 72 of 142 `timeout` Unknowns in the
  positional form, more than cvc5 decides in the sequence form ADR 0050 adopted it for, at no
  dependency. The part of criterion 5 that adds a solver keeps its gate: ADR 0050 is not clarified,
  and Bitwuzla gets the measured line in ROADMAP's post-MVP list.
- Deviation: criterion 3 asks Bitwuzla the positional file. It answers `unknown` to an equality over
  an uninterpreted sort and crashes on an array of one, and the product has both whatever the trace
  is. The spike also runs it on the file with each `declare-sort` written as a `define-sort` of a
  64-bit vector, and reports both rows. That is one line of text per sort, not the heap re-encoded
  (the size guard): every array, function and assertion is as printed.
- Decision: the product is `ProductEncoder.Encode`'s own. Two more `FragmentEncoder`s over the
  encoding's sorts and call encoders give the call sites and exits `Encode` does not hand out, and
  the query is rebuilt from them. Given production's own conjuncts the rebuild must be the very term
  `Encode` returned; the tool checks that on every query.
- Decision: the exception type, an `Int` in production's query, is a bv32 in the positional one.
  Without that the query is not "bit-vectors, arrays and uninterpreted functions only".
- Decision: trace equality is pairwise (every old site against every new site that can stand at the
  same position), not an array indexed by position. It needs no extensionality and no constant
  array, which cvc5 rejects in 49 files already.
- Decision: cvc5 is also run on the sequence file in the same pass, as the control. P1-025's 59 were
  measured at another commit and under another load; here it is 56.
- Decision: which query Z3 gives up on is read from P1-025's `results.tsv`, and Z3 is asked that
  query again in both encodings. It still gives up on all 142 in the sequence form.
- For a `divergence` query proved unsatisfiable, rung 1's other queries are asked of Z3 and then of
  the other solvers, which P1-025 did not do. It settles the two P1-025 left open: `opaque` is
  satisfiable on both (cvc5).
- The positional form is quadratic in call sites: up to 325,266 pairs of sites in one query, and no
  solver decides any query above 10,000. P1-038 carries a cap.
- Toolchain: a bash heredoc holding an apostrophe does not parse here; the report was written as a
  file. The run reads the corpus checkout, solvers and `results.tsv` of the P1-025 worktree's
  `.corpus/`, and writes under this worktree's.
