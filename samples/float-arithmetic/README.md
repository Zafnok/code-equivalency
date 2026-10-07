# float-arithmetic

Abstraction refinement (ADR 0053, ticket P1-030). Every floating-point operator is a pure function
both sides share, with no meaning (ADR 0025). A pair that rewrites arithmetic therefore gives the
solver a divergence that depends on how it reads the operator, which is Unknown(abstraction) (ADR
0026). When everything such a candidate depends on is interpretable, the pair is asked again with
those functions given their real meaning, IEEE 754 for `float` and `double`. The result's
`proofMethod` is then `bounded+refined`, and `properties.refined` lists the functions interpreted.

- `Arithmetic.Twice(double)`: `a * 2.0` against `a + a`. Doubling is exact in binary floating
  point, for every value including NaN, the infinities and the subnormals. Equivalent.
- `Arithmetic.IsOrdered(float, float)`: `low <= high` against `high >= low`. Both are false when
  either operand is NaN. Equivalent.
- `Arithmetic.Combine(double, double)`: `a + b` against `a - b`. Divergent, and the model is two
  numbers whose sum and difference differ.
- `Arithmetic.Sum(double, double, double)`: `(a + b) + c` against `a + (b + c)`. Floating-point
  addition is not associative: the two round at different points. Divergent, with three numbers
  that show it.
- `Arithmetic.Mean(double, double)`: `(a + b) / 2.0` against `(b + a) / 2.0`, each converted to
  `Celsius` by a user-defined conversion. The candidate depends on that conversion as well as on
  the arithmetic. A user-defined operator has a body, not a meaning the backend may assume, so
  nothing is interpreted and the pair stays Unknown(abstraction), naming
  `op:Equiv.Samples.FloatArithmetic.Celsius::op_Implicit(double)`. Its true verdict is Equivalent:
  addition commutes. Interpreting the interpretable part of such a candidate is ADR 0053's first
  rejected alternative, left for a measurement.
- `Celsius(double)` and the conversion itself are the same source on both sides. Equivalent by
  congruence.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Arithmetic.Twice(double)` | Equivalent (`bounded+refined`) |
| `Arithmetic.IsOrdered(float, float)` | Equivalent (`bounded+refined`) |
| `Arithmetic.Combine(double, double)` | Divergent (`bounded+refined`) |
| `Arithmetic.Sum(double, double, double)` | Divergent (`bounded+refined`) |
| `Arithmetic.Mean(double, double)` | Unknown (abstraction): the candidate depends on a user-defined conversion |
| `Celsius(double)`, `Celsius.op_Implicit(double)` | Equivalent (congruence) |

Exit code: 1 (a new Divergent result).
