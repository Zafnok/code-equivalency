# cleanup-extract-method

A cleanup commit that moves code between a method and a private helper (ticket P2-049). Both sides
target .NET 10 (ADR 0040), so no runtime rule applies. Both pairs behave the same on every input.

- Extract method: legacy `Invoice.Total` computes its discount inline. Modern moves that into the new
  private helper `Discount` and calls it.
- Inline method: legacy `Invoice.Shipping` calls the private helper `Billable`. Modern inlines it into
  `Shipping`, its only caller, and deletes it.

The snapshot records what `equiv` says today, not what it should say. A call is an observable
call-trace event (ADR 0018), and a helper that exists on one side only has no counterpart to pair it
with, so each caller is Divergent on the trace alone: the counterexample returns the same value on
both sides.

## Expected verdicts

| Procedure | Verdict today | Reason | Owner |
|---|---|---|---|
| `Invoice.Total(int, int)` | Divergent (a precision bug) | modern's trace has a call to `Invoice::Discount(int)`, which legacy does not have | P2-097 |
| `Invoice.Shipping(int)` | Divergent (a precision bug) | legacy's trace has a call to `Invoice::Billable(int)`, which modern does not have | P2-097 |
| `Invoice.Discount(int)` | Added | | |
| `Invoice.Billable(int)` | Removed | | |

Exit code: 1 (two Divergent results; Added and Removed do not affect the exit code).
