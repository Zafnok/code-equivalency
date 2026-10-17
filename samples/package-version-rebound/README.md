# package-version-rebound

Two versions of one library, as a package upgrade brings them (ADR 0061, ticket P2-179). Each side
is two projects: the application (`App`) and the library it references (`Text`). The library's
assembly name is the same on both sides, at two versions: 0.0 (no version attribute) for .NET
Framework 4.8 and 2.0 for .NET 10. Between the versions its extension class was renamed from
`TextExtensions` to `StringExtensions`, so the same source text in `App` binds to another member on
each side: a rebound call (ADR 0042).

- `Probe.Loud(string)` is `s.Shout()` on both sides. The two `Shout` members run the same IL: the
  same instructions over the same framework calls (`String.ToUpperInvariant`, `String.Concat`), and
  no runtime-changes row names either. So the two are one call. The legacy body calls
  `StringExtensions::Shout` where it called `TextExtensions::Shout`, no call is opaque, and the
  pair is Equivalent, with the pair in `properties.reboundCallsProved`.
- `Probe.Neat(string)` is `s.Tidy()` on both sides, but version 2.0's `Tidy` appends `"."`. Its IL
  is not the same, so the call stays a rebound call: each side lowers it as an opaque with reason
  `rebound-call`, and the pair is Unknown (`opaque`, scope `line`), with the pair in
  `properties.reboundCalls`.
- The library's own members are each on one side only, under their class's name: Removed on the
  legacy side and Added on the modern side.

## Expected verdicts

| Procedure | Verdict | Rebound pair |
|---|---|---|
| `Probe.Loud(string)` | Equivalent | `reboundCallsProved`: `TextExtensions::Shout(string)` and `StringExtensions::Shout(string)` |
| `Probe.Neat(string)` | Unknown (`opaque`, `rebound-call`) | `reboundCalls`: `TextExtensions::Tidy(string)` and `StringExtensions::Tidy(string)` |
| `TextExtensions.Shout(string)`, `TextExtensions.Tidy(string)` | Removed | none |
| `StringExtensions.Shout(string)`, `StringExtensions.Tidy(string)` | Added | none |

Exit code: 0 (Unknown does not fail the run without `--fail-on unknown`).
