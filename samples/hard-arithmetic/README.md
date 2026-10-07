# hard-arithmetic

A pair whose proof Z3 cannot find while the multiplications are multiplications, and finds at once when
they are any function both sides share (ADR 0025, clarification of 2026-10-07; ticket P1-031). Both sides
target .NET 10.

- `Crate.Volume` is a product of three 64-bit unknowns. The legacy side picks the first factor, `width` or
  `widthInches`, and then multiplies; the modern side branches first and multiplies on each path, with the
  second path's factor in a local of another name. The two are equal whatever multiplication does, since
  each path applies it to the same operands. Z3 has to show two 64-bit multipliers equal to prove it, and
  runs out of its resource limit on rung 1's `divergence` query (the sample tests pin `resourceLimit` to
  5,000,000; thorough mode's 30,000,000 does not prove it either). Rung 1 then asks again with `*` encoded
  as an uninterpreted function of its two operands, and that query is unsatisfiable in one round. The
  result is Equivalent with `proofMethod: bounded+abstracted`, and its `ladderTrace` holds the timeout and
  the round.
- `Crate.Weight` is the same pair with one factor changed: the modern side's second path multiplies by
  `height` twice and never by `density`. Z3 finds an input on the exact product, so the abstraction is not
  asked.

A renamed local alone does not make the pair hard: the two sides' products are then one term after the
definitions are substituted, and the exact query is unsatisfiable without the multiplier being looked at.
What makes it hard is that the operands are equal only by reasoning (here, on the branch), which is the
shape an edit near unchanged arithmetic leaves.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Crate.Volume(bool, long, long, long, long)` | Equivalent (`bounded+abstracted`) |
| `Crate.Weight(bool, long, long, long, long)` | Divergent |

Exit code: 1 (one Divergent result).
