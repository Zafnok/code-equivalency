# il-fallback

The IL lowering (ADR 0039, ticket P1-016; as a pass of thorough mode, ADR 0049, ticket P1-032). Each method pair is one the IOperation lowering leaves
with an opaque on the modern side only, and whose two sides compile to the same IL:

- `Nullables.Add(int?, int)`: the legacy side spells out the lifted `+` (`HasValue`, then
  `GetValueOrDefault() + b` wrapped in a `new int?`, else `null`). The modern side writes `a + b`,
  a lifted `Binary` and a nullable `Conversion` the IOperation lowering leaves opaque.
- `Nullables.Wrap(int)`: the legacy side writes `new int?(x)`, the modern side the implicit
  nullable `Conversion` of `x`, which the IOperation lowering leaves opaque.

From their IOperation bodies both pairs are Unknown (`opaque`), from the modern side's opaque nodes.
That is the first pass's result, and what `--mode quick` reports. Neither pair is congruent and each
holds an opaque the other side lacks, so both sides of both pairs are also lowered from IL, and the
IL bodies hold no opaque.

- In thorough mode, the default, the pairs keep both lowerings. The IL pass verifies the two that
  are still Unknown from their IL bodies and proves both (`decidedBy: il-pass`, `lowering: il`).
  `expected.sarif.json` is that run.
- With `--mode quick --il-fallback` the IL bodies replace the IOperation ones (`lowering: il`) and
  the first pass proves both. The census counts `pairsIlFallbackTried: 2` and
  `pairsLoweredFromIl: 2`. `IlFallbackSampleTests`'s snapshot is that run.

## Expected verdicts

| Procedure | `--mode quick` | thorough (default), or quick with `--il-fallback` |
|---|---|---|
| `Nullables.Add(int?, int)` | Unknown (`opaque`) | Equivalent (`lowering: il`) |
| `Nullables.Wrap(int)` | Unknown (`opaque`) | Equivalent (`lowering: il`) |

Exit code: 0 (both pairs are Equivalent).
