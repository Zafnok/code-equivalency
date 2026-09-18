# M3-002 Loop ladder rungs 1 to 3, timeouts, Unknown
Status: todo
Effort: L
Depends on: M3-001

## Goal
Loops and recursion are verified by rungs 1 to 3 of VERIFICATION-MODEL section 5.1:
bounded unrolling to k with `assume false` on the last back edge; lockstep relational
induction for loop pairs aligned by CFG position and normalised guard (unbounded
Equivalent); k-induction as the fallback for aligned pairs. Solver timeout yields
Unknown(timeout); IrOpaque flowing into an output yields Unknown(opaque); a non-aligned
loop pair that rung 1 cannot refute yields Unknown(unaligned-loop) until P1-001.
Every result carries proofMethod, and boundedBy when rung 1 proved it. A test per rung
and per Unknown reason; the soundness harness and ladder-monotonicity property from
section 7 run against each rung.

## Spec references
VERIFICATION-MODEL.md sections 1, 5, 5.1, 7; ADR 0008.

## Deliverables
- [ ] expand into concrete items before coding (see tickets/README.md template)
- [ ] tests: unit per rung, property (soundness, monotonicity), snapshot for the loop-bound-change sample

## Out of scope
Rungs 4 and 5 (P1-001, P1-002). Termination proofs.

## Notes
