# P1-001 Loop ladder rung 4: constrained Horn clauses via Z3 Spacer
Status: todo
Effort: L
Depends on: M3-003

## Goal
For loop pairs that rungs 1 to 3 leave Unknown(unaligned-loop), encode each loop as a
recursive predicate over its live variables, form the relational CHC system for the
pair (the Reve/LLReve construction), and solve it with Z3's Spacer fixedpoint engine.
A model is a coupling invariant and yields unbounded Equivalent with proofMethod chc;
a refutation yields Divergent with a trace; timeout yields Unknown(chc-timeout).
Soundness and monotonicity harnesses extended to this rung. Benchmarked on the
loop-to-LINQ and loop-fusion samples added in this ticket.

## Spec references
VERIFICATION-MODEL.md section 5.1 rung 4; ADR 0008.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit, property (soundness, monotonicity), snapshot for two new samples

## Out of scope
Rung 5. Any change to rungs 1 to 3.

## Notes
