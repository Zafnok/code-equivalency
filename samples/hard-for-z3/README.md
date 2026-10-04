# hard-for-z3

A pair whose one divergence Z3 does not find at the default budget, and cvc5 does (ADR 0050, ticket P1-033).
Both sides target .NET 10.

- `Vault.Opens` returns `false` on the legacy side. On the modern side it returns `true` when the product of
  its two arguments, each greater than 1, is 4503597865762987. That number is 16777213 times 268435399, both
  prime, so exactly two inputs differ, and finding one is factoring a 52-bit number through a 64-bit multiplier.

Z3 runs out of its resource limit (`resourceLimit`, 5,000,000) on rung 1's `divergence` query, so without a
second solver the pair is Unknown(timeout). That is what `expected.sarif.json` pins, and what every run without
`solvers.cvc5.path` in `equiv.config.json` gives.

With cvc5 configured, the query Z3 gave up on is printed and sent to cvc5, which answers `sat` in under a
second. Z3 completes the model from cvc5's values, the replay returns `false` on one side and `true` on the
other, and the pair is Divergent with `proofMethod: bounded+cvc5`. `Cvc5IntegrationTests` checks that where a
cvc5 executable is configured (`EQUIV_CVC5`, set on CI's Windows leg).

## Expected verdicts

| Procedure | Verdict without cvc5 | Verdict with cvc5 |
|---|---|---|
| `Vault.Opens(uint, uint)` | Unknown (`timeout`) | Divergent (`bounded+cvc5`) |

Exit code: 0 (no Divergent result without cvc5; an Unknown does not fail the run).
