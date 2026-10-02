# partly-compiling-modern

The modern side does not compile, as the raw output of a migration tool usually does not (ticket
P2-085; ADR 0029 as clarified on 2026-10-01). The tool retargeted the project to net10.0 and did
not carry `Regional.cs` over, so `Pricing.Currency` still calls a class the modern project does
not have. The compiler reports CS0234 on the `using` directive and CS0103 on the call.

Every reference of the modern project resolves, so the project is loaded and each method is decided
on its own. `Pricing.Currency` does not bind, so it is Unknown with reason `unbound`, the result
points at its first error, and the solver is never asked. The other three bind, and are compared as
they are bound:

- `Discount` was rewritten from an `if` to a conditional expression, and the solver proves it.
- `Net` is unchanged, so it is Equivalent by congruence.
- `HasCurrency` is unchanged and binds, though it calls `Pricing.Currency`. It is Equivalent by
  congruence, and its result lists `Pricing.Currency` under `unprovenAssumptions` (ADR 0019): the
  verdict holds if `Pricing.Currency` turns out equivalent, which this run could not say.

`Regional.Currency` exists on the legacy side only, so it is Removed.

On the legacy side the same two errors would still skip the project (exit 4): the legacy
application is the reference, and a name that does not bind there means it was loaded wrongly.

## Expected verdicts

| Procedure | Verdict | `unprovenAssumptions` |
|---|---|---|
| `Pricing.Net(int)` | Equivalent (by congruence) | |
| `Pricing.Discount(int)` | Equivalent (solver) | |
| `Pricing.Currency()` | Unknown (`unbound`) | |
| `Pricing.HasCurrency()` | Equivalent (by congruence) | `Pricing.Currency` |
| `Regional.Currency()` | Removed | |

Exit code: 0 (an Unknown result fails the run only with `--fail-on unknown`; no project is skipped,
so it is not 4).
