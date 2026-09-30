# il-fallback

The IL fallback (ADR 0039, ticket P1-016). Each method pair is one the IOperation lowering leaves
with an opaque on the modern side only, and whose two sides compile to the same IL:

- `Nullables.Add(int?, int)`: the legacy side spells out the lifted `+` (`HasValue`, then
  `GetValueOrDefault() + b` wrapped in a `new int?`, else `null`). The modern side writes `a + b`,
  a lifted `Binary` and a nullable `Conversion` the IOperation lowering leaves opaque.
- `Nullables.Wrap(int)`: the legacy side writes `new int?(x)`, the modern side the implicit
  nullable `Conversion` of `x`, which the IOperation lowering leaves opaque.

Without `--il-fallback` both pairs are Unknown (`opaque`), from the modern side's opaque nodes;
`expected.sarif.json` is that run. With `--il-fallback`, neither pair is congruent and each holds
an opaque the other side lacks, so both sides of both pairs are lowered again from IL. The IL bodies
hold no opaque, so both pairs keep them (`lowering: il`) and the solver proves both. The census
counts `pairsIlFallbackTried: 2` and `pairsLoweredFromIl: 2`. `IlFallbackSampleTests`'s snapshot
is that run.

## Expected verdicts

| Procedure | Without `--il-fallback` | With `--il-fallback` |
|---|---|---|
| `Nullables.Add(int?, int)` | Unknown (`opaque`) | Equivalent (`lowering: il`) |
| `Nullables.Wrap(int)` | Unknown (`opaque`) | Equivalent (`lowering: il`) |

Exit code: 0 (Unknown does not fail the run without `--fail-on unknown`).
