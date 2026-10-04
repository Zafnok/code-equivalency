# P1-026 Spike: can a solver Equivalent be checked by an independent proof checker?
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-025

## Goal
An Equivalent from the solver rests on Z3 answering unsatisfiable. P2-059 found Z3's Spacer giving
that answer wrongly once. A proof certificate removes the solver from what has to be trusted: cvc5
writes an Alethe proof of the same query and Carcara checks it. ROADMAP's post-MVP list has this as
low priority. Measure whether it works on this tool's queries at all, and what it costs.

State the limit in the report: a certificate covers the query, not the lowering or the encoder that
produced it, and an Equivalent by congruence (94.6% of matched pairs on the three large runs) has no
query to certify. M0-012's differential gate is what covers the rest.

## Spec references
ADR 0005, ADR 0002, ADR 0017, ADR 0036 (a checker admits, a proposer does not), ROADMAP post-MVP
("A second solver ... Alethe proof certificates checked by Carcara"), P2-059, P1-025 (the exporter).

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/spikes/proof-certificates/` reuses P1-025's exporter on every result of
   `gitextensions-9860` with `proofMethod` `bounded` or `lockstep-induction` (239 results), and
   writes each unsatisfiable query of the proof as an SMT-LIB 2 file.
2. Each file is run with cvc5 producing an Alethe proof, and the proof is checked with Carcara. Per
   query: cvc5 unsatisfiable or not, proof written or not, Carcara accepts, rejects or reports holes
   (steps it does not check), cvc5 time, checking time, proof size.
3. `docs/runs/<date>-proof-certificate-spike.md` gives the counts per outcome, the median and largest
   time and size, the pairs all of whose queries are accepted with no hole, and the limit stated in
   the Goal. Identities and counts only.
4. Unless no pair is certified with no hole, write the ADR (an opt-in `--certify`, where the
   certificate goes in SARIF, the ADR 0002 rows, and what a pair with holes reports) and its ticket.
   Only if nothing certifies, add one measured line to ROADMAP's post-MVP list, replacing the
   unmeasured one.
5. Nothing under `src/` changes. No binary is committed.

## Files
`tools/spikes/proof-certificates/**`, `docs/runs/<date>-proof-certificate-spike.md`, and either
`docs/adr/NNNN-*.md` with its README row and a ticket, or `docs/ROADMAP.md`.

## Tests
A `--self-test` on one sample pair: its queries export, cvc5 proves them and Carcara accepts.

## Size guard
Z3's own proof objects, Lean, or any proof of the encoder: stop. Any edit under `src/`: stop.

## Out of scope
Certifying rung 4 and rung 5 invariants (they are already checked rule by rule, P2-059).
Certificates for Divergent results (replay is their check).

## Notes
- From the 2026-10-03 improvement review (the "Checkable proof certificates" row). The review says a
  certificate makes a result auditable without trusting the IR and the encoder; it does not, see the
  Goal.
