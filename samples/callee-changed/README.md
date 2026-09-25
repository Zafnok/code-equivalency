# callee-changed

A caller nobody touched, over a callee that changed (ADR 0019, ticket M3-015). `Total(int a) =>
Tax(a) + a` is identical on both sides; `Tax` divides by 10 on the legacy side and by 5 on the
modern side.

Verdicts are modular. `Total` is Equivalent by congruence (ADR 0024): its bound bodies are the same,
and it treats `Tax` as a function both sides share. That verdict assumes `Tax` is equivalent, which
this run did not prove, so the result says so in `properties.unprovenAssumptions` and in one more
sentence of its message. No verdict and no exit code changes because of the assumption: `Tax`'s own
Divergent result is what fails the run.

## Expected verdicts

| Procedure | Verdict | `assumedCallees` | `unprovenAssumptions` |
|---|---|---|---|
| `Pricing.Tax(int)` | Divergent | | |
| `Pricing.Total(int)` | Equivalent (by congruence) | `Tax` | `Tax` |

Exit code: 1 (a new Divergent result).
