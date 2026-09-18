# ADR 0008: Loop ladder instead of bounded-only verdicts

Status: accepted (2026-09-17)

## Context
ADR 0005 left loops bounded. Regression verification (same language family, mostly
identical code) is tractable well beyond that: aligned loops can be proved equivalent
by relational induction with no invariant at all, and the remaining cases have
automatic invariant synthesis available in the Z3 we already ship.

## Decision
Loops are verified by a fixed ladder (VERIFICATION-MODEL.md section 5.1): bounded
unrolling, lockstep relational induction, k-induction (all MVP, M3-002), then
constrained Horn clauses via Z3 Spacer (P1-001), then LLM-proposed invariants that Z3
checks (P1-002). Every result records the rung that proved it. Unbounded Equivalent is
an MVP verdict for aligned loops.

## Why
- Lockstep induction is the SymDiff / Godlin-Strichman result; it needs only the
  product-program encoder we already build, so it is cheap to add in M3.
- Spacer is inside the Microsoft.Z3 package; no new dependency for rung 4.
- An LLM guess checked by Z3 is sound by construction; it can only add proofs, never
  wrong ones. It stays off by default because it adds cost and a network dependency.

## Rejected
- Raising the unrolling bound to thousands: formula size grows with k; rung 2 gives
  the unbounded answer for the same loops at constant cost.
- Boogie as the ladder host: still deferred (ADR 0005); rungs 1 to 4 are expressible
  directly in Z3.

## Consequences
M3-002 grows from M to L. Two P1 tickets exist. EQ006 and the runtime-changes table
(M2-006) are added because assumed-equal BCL calls are the larger false-proof risk
once loops are handled.
