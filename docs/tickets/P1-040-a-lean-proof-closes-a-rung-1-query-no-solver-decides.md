# P1-040 A Lean proof a model writes closes a rung 1 query no solver decides
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-038, P1-033, and ADR 0051 accepted

## Goal
P1-037 measured that a model closes two rung 1 `divergence` queries as Lean theorems, one of them
with a proof Lean's kernel checks alone (`docs/runs/2026-10-04-lean-vc.md`). Both are queries Z3
proves once the trace is compared by position, so the spike showed the road is open and not that
it leads anywhere. This ticket first asks whether it does, on the queries still undecided once
P1-038 and P1-033 are in, and builds ADR 0051 only if the answer is yes.

## Spec references
ADR 0051 (proposed), ADR 0005, ADR 0036, ADR 0049 decision 3, ADR 0050 (`ISmtSolver`, the printed
query), ADR 0017, ADR 0002, `tools/spikes/lean-vc/` (the translator's operator table and the
admission check: the shape, not the code to keep), `docs/runs/2026-10-04-lean-vc.md`,
`Equiv.Verify.Z3`'s invariant proposer for how a model is called.

## Design
- **Gate first.** With P1-038 and P1-033 merged, take a `full` run of `gitextensions-8522`, export
  the rung 1 queries no configured solver decides, and run `tools/spikes/lean-vc/` on them as
  P1-037 did. If no query is closed, write the count into `docs/runs/`, set ADR 0051 to withdrawn,
  and finish the ticket there.
- Past the gate, build ADR 0051's decisions 1 to 6: `IProofChecker` in `Equiv.Core`,
  `Equiv.Verify.Lean` running the `lean` executable `provers.lean.path` names, the translator in
  `Equiv.Verify.Z3` beside the SMT-LIB printer, `--proof-model`, and the two `proofMethod`
  suffixes.
- A theorem over the size Lean elaborates in the configured time is not sent; neither is one over
  what the model reads. Both are the timeout they were.

Pitfalls.
- The translation is trusted. Every operator the translator emits is tested against Z3's own value
  on edge operands (a zero divisor, a shift past the width, the sign bit), as the spike's self-test
  does.
- A `bv_decide` proof is not kernel-checked. It gets its own suffix and is never counted as `+lean`.
- The statement Lean compiles is the generated one. The proof is text after `:= by`, with no
  command in it.
- The spike's three undecided queries were, by the model's own account, satisfiable. A query that
  is not a theorem costs five rounds and millions of tokens for nothing; ask the configured solvers
  for a model first and spend the proof budget only where none is found.

## Acceptance criteria (all must hold; nothing beyond them)
1. `docs/runs/<date>-lean-gate.md` says how many rung 1 queries no configured solver decides after
   P1-038 and P1-033, and how many of them a Lean proof closes, by axioms used.
2. If that number is zero: ADR 0051 is `withdrawn`, ROADMAP's post-MVP line says so, and criteria 3
   to 6 do not apply.
3. A query closed by an admitted proof ends as unsatisfiable and the rung asks its next query; a
   rejected or missing proof leaves the result as it was. Unit tests cover both, with a stub
   `IProofChecker`.
4. An integration test proves one hand-written pair's query through a real Lean, skipped when no
   Lean is configured.
5. Without `--proof-model`, no model is called and no result changes: the `business-layer`
   snapshot is unchanged.
6. The SARIF of a result a proof touched carries the `proofMethod` suffix and a `ladderTrace` step
   naming Lean's version, the model id and the rounds.

## Files
`docs/runs/<date>-lean-gate.md`; past the gate `src/Equiv.Core/IProofChecker.cs`,
`src/Equiv.Verify.Lean/**`, `src/Equiv.Verify.Z3/**`, `src/Equiv.Cli/**`, their tests,
`docs/ARCHITECTURE.md`, `docs/VERIFICATION-MODEL.md`, `docs/adr/0002-dependencies.md`.

## Tests
Criteria 3 to 6.

## Size guard
A Lean semantics of the IR, or a proof about the lowering or the encoder: stop. A second encoder:
stop.

## Out of scope
Rungs 2 to 5. Shipping Lean. Looking for counterexamples in Lean.

## Notes
- Written 2026-10-04 by P1-037, whose criterion 7 asks for it whenever a query closes with the
  model. Its own evidence does not ask for the build: see the gate.
