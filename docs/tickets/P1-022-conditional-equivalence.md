# P1-022 Conditional equivalence: a pair that is not Equivalent says under which inputs it is
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-013, M3-025

## Goal
A Divergent has one counterexample and an Unknown has a reason. Neither says how much of the input
space is fine. For `removed-null-check` the useful answer is "Equivalent when `name != null`": the
reviewer then asks only whether `null` can arrive. Verification modulo versions (Logozzo, Lahiri,
Fähndrich and Blackshear, PLDI 2014; citation from memory, check before relying on it) infers such
conditions from the two versions. Here the condition is a hypothesis that Z3 must admit (ADR 0036),
so a wrong one costs time and can never be reported.

When done, an EQ002 from the solver and an Unknown with reason `abstraction` carry
`properties.agreesWhen`: a predicate over the source parameters under which the pair is proved
Equivalent. The verdict stays what it was.

## Spec references
ADR 0036 (hypothesis and checker), ADR 0037 (its Rejected list names partition verdicts as needing
their own ADR), ADR 0014 (the two opaque queries), ADR 0026 (taint), ADR 0029 (`scope` and the residual
claim, which this complements: that one is over lines, this one over inputs), ADR 0021 (parameter
sharing), VERIFICATION-MODEL.md sections 5 and 6.

## Design
Candidates. For a pair, collect the Bool values of both bodies that depend only on shared inputs:
source parameters, a reference parameter's `null.<Sort>` shadow, and constants. That is every branch
condition, null test and comparison whose operands reach back to parameters through `IrBinary`,
`IrUnary` and `IrConst` only, with no call, map read of a `field.*` or `array.*` map, `IrPure` or
opaque in between. Each such value `c` gives two candidates, `c` and `not c`. Cap the set at 16
per pair, shallowest first.

Check. A candidate `p` is admitted when both hold on rung 1's product:
- `p` together with "some observable differs or a side reaches an unshared opaque" is unsatisfiable
  (ADR 0014's second query, so the claim is a proof, not a residual);
- `p` is satisfiable with the product's input constraints (no vacuous condition).

Report. `agreesWhen` is the disjunction of the admitted candidates, since each is proved alone. Drop
a candidate another admitted one implies (one implication query per pair of admitted candidates,
skipped above 6 admitted). Render it twice: `smt`, SMT-LIB over the shared inputs, and `text`, the
source spelling with the parameter names of the modern side (`name != null`, `count >= 0`).

Pitfalls.
- A pair with a loop or self-call: rung 1's product proves nothing past the bound. Leave such a pair
  without `agreesWhen` in this ticket.
- A side-specific function (a runtime-changed callee, a `:old`/`:new` contract function) is free in
  the model. A candidate is still sound, since it is checked against the same product, but on EQ006
  nothing is admitted in practice. Do not query EQ006.
- The counterexample must falsify the reported disjunction; assert it, and treat a failure as a bug
  (no `agreesWhen`, a `warning` notification).
- A candidate built from a parameter only one side has is not over shared inputs. Skip it.

## Acceptance criteria (all must hold; nothing beyond them)
1. A new ADR is merged before any code. It decides that a non-Equivalent result may carry a proved
   input condition, names the property, states that no verdict, rule id, exit code or fingerprint
   depends on it, and says how it relates to ADR 0037's rejection of partition verdicts.
2. `samples/removed-null-check`'s EQ002 carries `agreesWhen.text` `name != null` (with the sample's
   own parameter name), and its message ends with `Equivalent when <text>.` Snapshot checked in.
3. A pair with no admitted candidate carries no `agreesWhen`, and its message is unchanged.
4. The census reports the pairs queried, the pairs with an admitted condition and the time spent.
5. VERIFICATION-MODEL.md sections 5 and 6 describe the candidates, the two checks and the property.
6. The soundness property test of Tests below passes at 200 pairs per pull request.

## Files
`src/Equiv.Verify.Z3/Conditions/` (new: candidate harvest, the check, rendering),
`src/Equiv.Verify.Z3/Z3Backend.cs`, `src/Equiv.Core/` (the verdict records and `SarifReportWriter`),
their tests, one sample snapshot, `docs/adr/NNNN-*.md` and its `docs/adr/README.md` row,
`docs/VERIFICATION-MODEL.md`, `README.md`.

## Tests
`Candidates_AreOnlyPredicatesOverSharedInputs`, `Candidate_IsRejectedWhenAnOpaqueIsReachable`,
`Candidate_IsRejectedWhenVacuous`, `AgreesWhen_IsTheDisjunctionOfAdmittedCandidates`,
`AgreesWhen_DropsAnImpliedCandidate`, `AgreesWhen_IsFalsifiedByTheCounterexample`,
`LoopingPair_HasNoAgreesWhen`, `AgreesWhen_ChangesNoFingerprintOrExitCode`, the sample snapshot, and a
property test in `Equiv.Tests.Integration` on M0-012's generated pairs: for a pair with `agreesWhen`,
every generated input that satisfies it gives equal observables on the CLR.

## Size guard
More than 16 candidates per pair, a candidate over three or more inputs, or any synthesis beyond
harvesting predicates the bodies already compute: stop. That is a second ticket.

## Out of scope
Pairs with loops. Conditions over the heap or over a callee's result. Necessary conditions ("always
differs when"). A new rule id or verdict. Counting how many inputs satisfy the condition (P1-027).

## Notes
- From the 2026-10-03 improvement review (its second priority).
