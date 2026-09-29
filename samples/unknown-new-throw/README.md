# unknown-new-throw

An Unknown that says whether the modern side can now throw (ADR 0037, ticket P1-013). The legacy
`Report.Width(int count)` lowers fully and always returns. The modern `Width` first computes
`$"{count}"`, an interpolated string the lowerer leaves opaque and the legacy side does not have, then
adds a guard that throws `ArgumentOutOfRangeException` for a negative `count`, and returns the
string's length.

Every modern path reaches the unshared opaque node, so the pair is Unknown (`opaque`, scope `line`).
The two failure-refinement queries then compare only whether each side returns or throws:

- `newFailures: unknown`. The new throw lies past the opaque node, and an input that reaches an
  unshared opaque node has an unknown outcome (ADR 0014): nothing is proved, and no input on which
  neither side reaches it throws.
- `removedFailures: none-proved`. The legacy side reaches no opaque node and never throws, so the
  modern side cannot return where the legacy side throws.

The ticket asked for this sample with the guard *before* the opaque node and `newFailures: found`.
That pair is not Unknown: an input the guard rejects reaches no opaque node on either side, so ADR
0014's first query finds the divergence and the pair is Divergent (EQ002). A `found` answer needs
such an input, so on an Unknown it occurs only when rung 1's model was a tainted one (ADR 0026);
the backend's unit tests cover it on IR.

## Expected verdicts

| Procedure | Verdict | `unknownReason` | `newFailures` | `removedFailures` |
|---|---|---|---|---|
| `Report.Width(int)` | Unknown | `opaque` | `unknown` | `none-proved` |

Exit code: 0 (Unknown does not fail the run without `--fail-on unknown`).
