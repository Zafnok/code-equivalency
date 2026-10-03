# P2-118 Decide how code a source generator emits is compared and reported
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
`equiv` enumerates every method in a compilation, including the ones a source generator adds. The
regex generator emits a matcher for each `[GeneratedRegex]`, and what it emits depends on the target
framework. On P2-066's `jellyfin-13023` (net8.0 to net9.0), where no regex pattern was edited, the
namespace `System.Text.RegularExpressions.Generated` holds 137 results: 120 congruent, and 17 that
nobody wrote and nobody can review:
- 4 EQ002: `WhiteSpaceRegex_0.RunnerFactory.Runner::TryFindNextPossibleStartingPosition(System.ReadOnlySpan<char>)`
  and three `Utilities::IndexOfNonAsciiOrAny_<hash>(System.ReadOnlySpan<char>)`;
- 8 Unknown (4 `unaligned-loop`, 4 `abstraction`), 4 `unmatched-overload`, 1 Removed.

The generated helpers are also named by a hash of their content, so a helper whose text changes is
Removed on one side and unmatched on the other. Separately, each `[GeneratedRegex]` partial method
is a whole-body opaque pair with reason `no-body` (30 on this pair), because its body is in the
generated half.

Decide, through `equiv-adr`, what `equiv` does with generated code. Candidates: compare the
declaration that drives the generator (the pattern and options of `[GeneratedRegex]`) and treat the
emitted matcher as its implementation, congruent when the declaration is; keep comparing the emitted
code but report it in its own review group, ranked last; or skip generated trees and say so in
`run.properties`. Then implement the decision for the regex generator.

## Spec references
ADR 0018 (what is observable), ADR 0029 (nothing is dropped silently), P2-064 (review list groups),
P2-098 (generated build-info constants), `docs/runs/2026-10-03-upgrade-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The decision is recorded as the bar test says (an ADR, or a clarification on an existing one).
2. A sample pair with one unchanged `[GeneratedRegex]` method, legacy `net8.0` and modern `net9.0`,
   has no EQ002 and no Unknown that names generated code.
3. The same sample with the pattern edited on the modern side is not Equivalent.

## Tests
Integration tests on the two samples.

## Out of scope
Generators other than the regex one: list in `## Notes` which others the corpus pairs use.

## Notes
- Found by P2-066 on `jellyfin-13023`. P2-116 owns the three generated methods that crash lowering.
