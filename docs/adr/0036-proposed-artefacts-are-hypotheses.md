# ADR 0036: A proposed invariant, contract or table row is a hypothesis until a checker admits it; callee contracts replace unproven assumptions

Status: accepted (2026-09-24)

## Context
Several planned sources produce facts that the engine would like to use. P1-002 has a model
propose loop invariants. ADR 0035's harness produces execution traces. The same pattern works for
callee contracts, rewrite rules and catalogue entries. No rule yet says what may turn such a
proposal into evidence. Recent work shows the pattern is sound only when a checker gates it:
Lemur (Wu, Barrett and Narodytska, ICML 2024) for invariants, and *Partial Contracts Suffice*
(Charalambous, Menezes, Sun and Cordeiro, arXiv 2607.10291, 2026) for regression verification with
inferred, caller-sufficient contracts. Separately, ADR 0019 leaves a caller Equivalent with
`unprovenAssumptions` whenever a callee pair changed, even when the caller cannot observe the change.

## Decision
1. **Proposers propose; checkers admit.** Whatever a model, a heuristic or an execution trace
   produces is a hypothesis:
   - a loop invariant;
   - a callee contract;
   - an IR rewrite rule;
   - an `api-equivalences.json` entry;
   - a `runtime-changes.json` row.

   A hypothesis reaches a verdict only after its checker admits it:
   - invariants, contracts and rewrite rules: a solver proof, discharged in this run or in a
     checked-in test;
   - catalogue entries: a citation, as ADR 0020 requires;
   - runtime rows: a citation or a measured witness (ADR 0035).

   Every result that uses an admitted hypothesis names the proposer (`properties.proposedBy`) and
   the checker (`proofMethod`). No proposer output is ever evidence by itself.
2. **Caller-sufficient callee contracts.** When a matched callee pair (f, f') is not
   Equivalent, a proposer may offer a *relational* contract K. K ranges over the shared
   arguments, the heap at the call, and each side's own outcome and heap effect: for example
   `(r > 0) == (r' > 0)`. A side's outcome is its return value together with its `threw` flag and
   exception type. If the solver proves that the product program of f and f' satisfies K,
   the caller pair is re-verified with K in place of the shared uninterpreted function. Each
   side's call gets its own fresh outcome and heap, constrained jointly only by K. If the caller is
   then Equivalent, `proofMethod` is suffixed `+contract`, and the callee moves from
   `unprovenAssumptions` to `properties.contractsUsed`. The proof of K is itself modular (ADR
   0019): it assumes f's own matched callees equivalent. So the caller inherits f's
   `unprovenAssumptions`, and those of every callee whose contract it uses. This is sound: the
   caller agrees on every pair of callee behaviours that satisfy K, and the real pair satisfies K
   whenever the assumptions the caller still lists hold.

## Why
- One rule covers every current and future proposer: P1-002, trace mining, contracts, and model
  suggestions for the catalogue. Each new source then needs no new soundness argument.
- LLMs are unreliable judges of equivalence (EquiBench, 2025) but useful sources of candidates.
  Search is cheap to trust when a small, trusted checker gates it.
- A changed callee is the most common reason a proved caller carries an unproven assumption. A
  caller-sufficient contract needs no full specification of the callee, only what the caller uses.

## Rejected
- **Letting a model's confidence or majority vote decide a verdict:** there is no soundness
  argument, and it cannot be reproduced.
- **Full functional specifications for callees:** real code never has them, and the caller
  rarely needs them.
- **Sharing one havoced result between both sides under K:** that would assume the two callees
  return the same value, which is exactly what is not known.
- **Per-callee (unary) contracts, one for f and one for f':** they cannot express "the caller
  sees the same thing from both". A unary contract strong enough for that is a near-complete
  specification.

## Consequences
- P1-002 is reworded to cite this ADR. New tickets P1-008 (trace-mined invariants) and P1-009
  (contracts).
- VERIFICATION-MODEL.md section 1 (modular verdicts) and section 6 (`proposedBy`, `contractsUsed`,
  the `+contract` suffix) change once this ADR is accepted.
- The contract encoding must give each side its own call result, `threw` flag and heap. Reusing
  any shared function, the shared `threw` included, would silently turn the contract into an
  equality assumption. P1-009's soundness
  property targets exactly that mistake.
