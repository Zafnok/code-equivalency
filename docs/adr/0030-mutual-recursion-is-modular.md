# ADR 0030: Mutual recursion is verified modularly; Unknown(Recursion) is for self-recursion the ladder cannot decide

Status: proposed (2026-09-23)

## Context
Ticket M3-002 asks for a fixture where mutual recursion gives `Unknown(recursion)` ("mutual recursion is
Unknown(recursion) in this ticket"). `IVerificationBackend.Verify` receives one pair of bodies. A call to another
procedure is an uninterpreted function both sides share (VERIFICATION-MODEL.md section 5, ADR 0019), so in `F`'s body
a call to `G` looks the same whether or not `G` calls `F` back. The backend cannot see a cycle through another
procedure; only the CLI, which holds every matched pair, has the call graph. An IR fixture holds one procedure per
side, so no fixture can show mutual recursion to the backend either. M3-002 therefore ships `recursion-unaligned`
(self-recursion the ladder cannot decide, `Unknown(Recursion)`) in place of the mutual-recursion fixture.

## Decision
Mutual recursion stays under ADR 0019's modular reading. Each pair's verdict assumes that the matched callee pairs its
bodies call are equivalent, and for a cycle of matched procedures that assumption is the mutual-summary proof rule
(Godlin and Strichman, "Regression verification", DAC 2009): if every pair in the cycle is Equivalent under shared
call functions, every pair is partially equivalent. A pair in the cycle that is not Equivalent is already named in
`properties.unprovenAssumptions` (M3-015) of the others. `UnknownReason.Recursion` means a self-recursive pair that no
rung of the loop ladder decided.

## Why
- Sound without new machinery: the rule that justifies ADR 0019 for ordinary callees covers cycles, by induction on
  the recursion depth of terminating runs.
- The backend contract stays the pair it is; nothing new crosses from the CLI.
- Self-recursion is visible (a call to the procedure's own identity), and M3-002 handles it: rung 1 inlines it `k`
  deep, rung 2 treats the self-call as the shared call.

## Rejected
- **Pass the call graph (or the procedures on a cycle) in `VerificationOptions` and return `Unknown(Recursion)`.** A
  Core contract change that only loses precision; soundness does not need it.
- **Inline other matched callees.** ADR 0019 rejected inlining for the modularity it discards and the blow-up on
  recursion.

## Consequences
- M3-002's mutual-recursion fixture is `recursion-unaligned` (self-recursion). Once accepted, VERIFICATION-MODEL.md
  section 5.1's recursion sentence gains "mutual recursion is modular (ADR 0019, ADR 0028)", and the ticket's Design
  sentence "mutual recursion is Unknown(recursion) in this ticket" is struck.
- Termination is still not claimed (section 5.1): partial equivalence only, as for loops.
