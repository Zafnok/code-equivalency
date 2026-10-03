# P2-103 A string constant holding a lone surrogate no longer crashes lowering
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
On P2-066's `jellyfin-13023`, three pairs fail to lower, each with one notification "failed: Cannot
transcode invalid UTF-16 string to UTF-8 JSON text":
- `System.Text.RegularExpressions.Generated.InvalidXMLCharsRegexRegex_0.RunnerFactory.Runner::TryMatchAtCurrentPosition(System.ReadOnlySpan<char>)`
- `System.Text.RegularExpressions.Generated.Utilities::IndexOfNonAsciiOrAny_054D88A1CA74F303E737A5D6CB3A2FC78EF8B4B74FED9DBE7BF41353919E123B(System.ReadOnlySpan<char>)`
- `System.Text.RegularExpressions.Generated.NonConformingUnicodeRegex_0::.ctor()`

All three are emitted by the regex source generator, which writes character ranges such as
`"𐏿"` as string constants. A string that is not well-formed UTF-16 cannot be written by
`System.Text.Json`, and something on the lowering path (a fingerprint, the census, or the log) writes
the constant as JSON. The run exits 5 and the three pairs are unverified. Minimal repro, as a sample
pair (identical file on both sides):

```csharp
static int Find(string s) => s.IndexOfAny("\uD800\uDBFF".ToCharArray());
```

Find the writer, and encode a constant so that every UTF-16 code unit survives (an escaped form, or
the code units as numbers), so that two equal constants still have equal encodings.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (`Literal`), ADR 0024 (fragment fingerprints),
`docs/runs/2026-10-02-upgrade-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The sample pair above lowers with no notification and is congruent.
2. Two constants that differ only in a lone surrogate do not get the same encoding (unit test).

## Tests
Integration test on the sample; the unit test in criterion 2.

## Out of scope
Lowering the generated regex code any further than it lowers today.

## Notes
- Found by P2-066: 3 on `jellyfin-13023`, in its census and in its `full` run.
