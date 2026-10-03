# forwarder-to-bcl

A helper that only forwards is replaced by the member it forwards to (ADR 0047, ticket P2-068). The
legacy side has a `Text` class of two one-line helpers and calls them. The modern side has no `Text`
and calls `string.IsNullOrWhiteSpace` directly.

- `Names.Name(string)` calls `Text.Blank(s)` on the legacy side. `Text.Blank` is a forwarder: a
  static method whose whole body is `string.IsNullOrWhiteSpace(s)`, its own parameter passed through
  unchanged. So the legacy call is lowered as the call to `String::IsNullOrWhiteSpace(string)`, which
  is what the modern side calls. The two bound fingerprints are then equal, the pair is Equivalent
  by congruence, and `properties.forwardersResolved` names the forwarder and its target. Before ADR
  0047 this was a false Divergent: two callee identities, two unrelated functions, two different
  trace events.
- `Names.Trimmed(string)` calls `Text.BlankTrimmed(s)` on the legacy side, whose body is
  `string.IsNullOrWhiteSpace(s?.Trim())`. It changes its argument, so it is not a forwarder and its
  body is not read (ADR 0019). The legacy side calls `Text::BlankTrimmed`, the modern side calls
  `String::IsNullOrWhiteSpace`, and the pair stays Divergent with no forwarder named.
- `Text.Blank(string)` and `Text.BlankTrimmed(string)` exist only on the legacy side, so each is
  Removed.

## Expected verdicts

| Procedure | Verdict | `forwardersResolved` |
|---|---|---|
| `Names.Name(string)` | Equivalent (by congruence) | `Text::Blank(string)` to `String::IsNullOrWhiteSpace(string)` |
| `Names.Trimmed(string)` | Divergent | none |
| `Text.Blank(string)` | Removed | |
| `Text.BlankTrimmed(string)` | Removed | |

Exit code: 1 (a new Divergent result).
