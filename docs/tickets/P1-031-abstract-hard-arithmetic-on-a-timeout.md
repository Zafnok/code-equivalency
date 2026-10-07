# P1-031 Abstraction refinement, part 2: a query that times out is asked again with its hard arithmetic abstracted
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-050, M3-016

## Goal
Solver budget is the second largest Unknown reason on the three large runs, 400 of 1,471, and
P2-050 found that 20 times the budget proves none of them. Bitvector multiplication, division and
remainder of two unknowns are what a bit-blasting solver is worst at, and in a migrated pair they
are nearly always the same on both sides. ARDiff (Badihi et al., FSE 2020; citation from memory,
check before relying on it) replaces such code with an uninterpreted function both sides share,
which is sound for a proof, and gives it its meaning back only where a counterexample turns out to
depend on it.

This is the opposite direction to P1-030: that one makes an abstraction more precise to decide an
`abstraction` Unknown, this one makes the encoding less precise to decide a `timeout` Unknown.

When done, a rung 1 query that hits its budget is asked again with those operators abstracted. An
unsatisfiable answer is a proof. A model is checked against the real arithmetic and either is a
counterexample or refines the abstraction.

## Spec references
ADR 0025 (a shared pure function proves unchanged arithmetic without its semantics), ADR 0026,
ADR 0008, VERIFICATION-MODEL.md sections 5 and 6 (budgets, `timeout`),
`docs/runs/2026-10-01-timeout-budget.md`, P2-101 (which theories the hard queries use; read its
report if it has landed), `src/Equiv.Verify.Z3/ProductEncoder.cs`, `Z3Backend.cs` (`Query`).

## Design
Abstracted operators: `IrBinary` multiplication, signed and unsigned division and remainder, where
neither operand is a constant, and the matching `IrOverflows` tests. Each becomes one uninterpreted
function per operator and width, shared by both sides, applied to the operands. Everything else is
encoded as today.

Loop.
1. Rung 1's query hits `resourceLimit` or `timeoutMs`. Encode the product again with the operators
   abstracted and ask with the same budgets.
2. Unsatisfiable: Equivalent. This is sound because every real run is a run of the abstracted
   product, with the functions being the real operators.
3. Satisfiable: replay the model's inputs in `IrInterpreter`, which computes real arithmetic. A
   replay that diverges untainted is Divergent with that model, exactly as a model of the original
   query would be.
4. Otherwise the model is spurious: some application `f(a, b)` in it has a value the real operator
   does not give. For each such application add the fact `f(a, b) = a op b` for the model's `a`
   and `b`, as constants, and ask again. At most 8 rounds.
5. Out of rounds, or a timeout in any round: Unknown(timeout), as today, with the rounds in
   `ladderTrace`.

Pitfalls.
- Step 4's facts are point facts and can loop forever on a query that needs the operator's algebra.
  The round cap is the answer; do not add commutativity or distributivity lemmas here.
- Abstracting an operator on one side only would be unsound for nothing and incomplete for
  everything. The function is per operator and width, never per side.
- Division's zero test and `MinValue / -1` test are separate `IrBinary` and `IrOverflows`
  instructions and stay exact, so exception behaviour is never abstracted away.
- The second query of ADR 0014 (opaque reachability) must be asked on the abstracted product too
  before an Equivalent is reported.
- Rungs 2 to 5 are not changed. A pair with loops takes this path only on rung 1's own queries.

## Acceptance criteria (all must hold; nothing beyond them)
1. `equiv-adr`'s bar test is run first and its outcome is in Notes. The expected vehicle is a
   clarification of ADR 0025 (integer operators may be shared functions when a query needs it) and
   of ADR 0026 (a model through such a function is decided by replay, not by taint).
2. A result decided this way has `proofMethod` suffixed `+abstracted`, and `properties.ladderTrace`
   holds one step per round with the number of facts added.
3. New sample `hard-arithmetic`: two bodies that compute the same product of three 64-bit unknowns
   and differ in an unrelated renamed local are Equivalent with `bounded+abstracted` at the default
   budget (and Unknown(timeout) with this ticket's path disabled, pinned by a test); a variant that
   changes one factor is Divergent.
4. A pair for which the abstraction is too coarse, `x * y` against `y * x`, ends Unknown(timeout)
   after the rounds, or Equivalent if Z3 decides the original; it is never Divergent.
5. Section 7's soundness harness runs with this path forced on for every pair: no mutant is
   Equivalent, and every Divergent's model replays.
6. On a corpus run of `gitextensions-8522` through `equiv-corpus-run`, Notes records how many
   `timeout` Unknowns are decided, by outcome, the added verify time, and that no result that was
   decided before changed.
7. VERIFICATION-MODEL.md sections 5 and 6 describe the path.

## Files
`src/Equiv.Verify.Z3/ProductEncoder.cs`, `Z3Backend.cs`, a new `src/Equiv.Verify.Z3/Refinement/`
(shared with P1-030 if it has landed), `src/Equiv.Core/` (`ProofMethod`), their tests,
`samples/hard-arithmetic/**`, `docs/VERIFICATION-MODEL.md`, the two ADRs (clarifications only).

## Tests
`AbstractedOperators_AreSharedByBothSides`, `ConstantOperand_IsNeverAbstracted`,
`DivisionGuards_StayExact`, `Abstracted_UnsatisfiableIsEquivalent`, `SpuriousModel_AddsPointFacts`,
`RealModel_IsDivergentAfterReplay`, `Refinement_StopsAfterEightRounds`,
`CommutedProduct_IsNeverDivergent`, the sample snapshot, and the harness run of criterion 5.

## Size guard
Algebraic lemmas, abstracting anything but the operators in the Design, or touching rungs 2 to 5:
stop and file a ticket with criterion 6's numbers.

## Out of scope
Abstracting unchanged statement blocks or whole callees as functions (ADR 0024's fragments and ADR
0019's callees already do this where they apply). Another solver (P1-025). A larger budget.

## Notes
- From the 2026-10-03 improvement review ("Abstraction refinement for hard arithmetic ... targets
  where P ≠ NP hurts"), scheduled by the user the same day.
- How many of the 400 timeouts hold these operators is not known; P2-101's report is the place to
  look first. If it shows under ten such pairs on `gitextensions-8522`, record that in Notes and
  build the sample path only.
