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
- The citation was checked: Logozzo, Lahiri, Fähndrich and Blackshear, "Verification modulo versions: towards usable
  verification", PLDI 2014, doi 10.1145/2594291.2594326.
- Decision: ADR 0048 is its own pull request (#387), as `equiv-adr` says, and this ticket's branch is stacked on it, so
  the ADR merges first (criterion 1).
- Decision: an admitted candidate that implies another is the one dropped. The Design says "drop a candidate another
  admitted one implies", which read literally keeps `a < 0` and drops `a < 5`. The condition is a disjunction, so the
  weaker candidate covers the stronger one's inputs and the stronger one is the redundant disjunct.
- Decision: "do not query EQ006" is decided in the backend, which has no runtime-change table: a pair whose model's
  call trace holds a callee flagged `RuntimeChanged` is not searched. The frontend sets that flag only for a row inside
  the pair's runtime interval, so this is every EQ006.
- Decision: `agreesWhen` also carries `proposedBy: harvested-predicates` and `proofMethod: bounded`. ADR 0036 decision 1
  requires every result that uses an admitted hypothesis to name its proposer and its checker.
- Decision: both renderings are printed from the harvested term, not by Z3. Z3 wraps and `let`-binds long terms, and the
  `parity` job compares result properties between Windows and Linux.
- Decision: a value through `trunc` is not harvested. A truncation has no one source spelling: the IR does not say
  whether the narrow type is signed. A value more than 8 operations deep is not harvested either: a term is the body's
  value graph as a tree, which can double in size per level.
- Decision: the size guard's "three or more inputs" is applied as a filter. Such a value is skipped, not checked. None
  was needed for a criterion, so the guard did not trip.
- Decision: in `text`, an unsigned comparison, division or remainder casts its operands (`(uint)x < 3`), a zero
  extension is a cast to the unsigned type of the operand's width, and a shift count is written without its widening.
  The first run of the property test found the last one: `c >> ((uint)(a & 63))` is not C#. With these, `text` compiles
  and evaluates as C# for `int`, `long`, `bool` and reference parameters, which is what the property test relies on.
  For a parameter declared unsigned or narrower than `int` the text can still read differently from `smt`, because
  `IrVar` carries no signedness. `smt` is the exact statement. A ticket that gives `IrParameter` its declared type
  would close that.
- The counterexample check binds the product's inputs to the decoded counterexample and asks the solver for the
  disjunction. It checks the reported inputs against the reported condition, not the model against itself.
- `samples/added-branch` now carries `Equivalent when x != 0`: the modern body's own guard is the candidate.
- The property test runs 200 pairs per pull request (criterion 6). It was also run once locally at the nightly budget
  (`EQUIV_DIFFERENTIAL_BUDGET=nightly`, 5,000 pairs) and passed. `mutation.yml`'s nightly `differential` job filters to
  `DifferentialSoundnessTests` alone, so it does not run `ConditionSoundnessTests`; adding the class to that filter is a
  workflow change outside this ticket's Files.
- Snapshots: 13 `samples/*/expected.sarif.json` and the two `*.execute.sarif` files changed. Seven results gained
  `agreesWhen` (`added-branch`, `api-drift`, `bcl-overload-rebinding`, `callee-changed-invisible`,
  `dependency-rebinding`, `effect-free-bcl-call`, `removed-null-check`, `repeated-edit`); the rest only gained the
  census entry. Three of the conditions are `<receiver> == null`: both sides throw the same exception there, which is
  true and of little use to a reviewer.
- Surprise: `IrBitVec` allows only widths 8, 16, 32 and 64, so the unsigned cast has four cases and no default.
