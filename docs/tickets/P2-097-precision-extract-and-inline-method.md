# P2-097 Precision bug: extracting or inlining a private helper makes its caller Divergent
Status: todo
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P2-049

## Goal
P2-049's sample `cleanup-extract-method` has the two commonest structural cleanups. Extract method:

```csharp
// legacy
public int Total(int quantity, int unitPrice)
{
    int subtotal = quantity * unitPrice;
    int discount = 0;
    if (subtotal > 100) { discount = subtotal / 10; }
    return subtotal - discount;
}
// modern
public int Total(int quantity, int unitPrice)
{
    int subtotal = quantity * unitPrice;
    return subtotal - Discount(subtotal);
}
private int Discount(int subtotal) { if (subtotal > 100) { return subtotal / 10; } return 0; }
```

Inline method is the reverse: legacy `Shipping` calls a private `Billable`, and modern has its body
in `Shipping` and no `Billable`.

Today both callers are Divergent (EQ002), and both pairs are behaviour-preserving, so both are false
positives. The counterexample returns the same value on both sides. The only difference is the
trace: one side records a call to the helper and the other does not (ADR 0018). The helper is Added
or Removed, so it has no counterpart on the other side, and no caller outside the assembly can see
whether it ran.

ADR 0019 rejects inlining matched callees, for modularity and because of recursion. An unmatched
helper is another case: it has no pair to be verified as, so its result has no verdict to rest on,
and the caller is the only place its body can be checked. Decide, through `equiv-adr`, how a call to
a solution-local callee that exists on one side only is treated. Candidates: inline it into the
caller under a depth and size bound; drop its trace event and summarise it; or report the caller as
Unknown with a new reason instead of Divergent.

## Spec references
ADR 0018 (the call trace is observable), ADR 0019 (modular verdicts; rejects inlining matched
callees), VERIFICATION-MODEL section 1 (the modular reading) and section 3 (call identity), P2-068
(a forwarder replaced by its target, the nearest open ticket: it covers a one-call helper that
forwards to a BCL member, not a helper with a body).

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change. It says which callees qualify
   (accessibility, virtual or not, recursion, size), what happens to the helper's own trace event,
   and what the SARIF reports.
2. `Invoice.Total(int, int)` and `Invoice.Shipping(int)` in `samples/cleanup-extract-method` are
   Equivalent, and each result names the helper that was resolved. `Discount` stays Added and
   `Billable` stays Removed. The sample's snapshot and README rows are updated.
3. A variant whose extracted helper tests `subtotal >= 100` is Divergent, with a counterexample
   whose return values differ.
4. A helper that is recursive, virtual, or over the decision's bound is not resolved, and its caller
   is not Equivalent.
5. A callee matched on both sides is treated as today (ADR 0019): the `callee-changed` and
   `callee-changed-invisible` snapshots do not change.

## Tests
- Integration test on `samples/cleanup-extract-method` and on the variants of criteria 3 and 4.
- Unit tests the decision names, in the affected test projects.

## Size guard
Inlining a callee that is matched on both sides contradicts ADR 0019: stop.

## Out of scope
A helper extracted into another class or made `public`. Extracting a local function or a lambda.
Forwarders to BCL members (P2-068).

## Notes
- Found by P2-049 (`samples/cleanup-extract-method`, `Invoice.Total` and `Invoice.Shipping`). Review
  groups `calls:...Invoice::Discount(int)` and `calls:...Invoice::Billable(int)`.
