# unknown-new-throw

An Unknown that says whether the modern side can now throw (ADR 0037, ticket P1-013). The legacy
`Report.Width(int count)` lowers fully and always returns. The modern `Width` first computes
`$"{count:D}"`, an interpolated string the lowerer leaves opaque because its hole has a format clause
(ticket P2-086) and the legacy side does not have, then adds a guard that throws
`ArgumentOutOfRangeException` for a negative `count`, and returns the string's length.

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

That Unknown is the first pass's result, and what `--mode quick` reports. Thorough mode, the default (ADR 0049,
ticket P1-032), goes on: the pair holds an opaque the legacy side lacks, so its IL pass verifies it again from IL,
where the interpolated string is a sequence of calls and not an opaque node. A negative `count` then reaches the
modern guard, which throws where the legacy side returns, and the pair is Divergent (`decidedBy: il-pass`). That is
the divergence the sample was written around. `expected.sarif.json` is the thorough run.

## Expected verdicts

| Procedure | Mode | Verdict | `unknownReason` | `newFailures` | `removedFailures` |
|---|---|---|---|---|---|
| `Report.Width(int)` | `--mode quick` | Unknown | `opaque` | `unknown` | `none-proved` |
| `Report.Width(int)` | thorough (default) | Divergent | | | |

Exit code: 1 (the Divergent result of the default, thorough run; `--mode quick` exits 0).
