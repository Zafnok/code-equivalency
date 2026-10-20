# P2-270 Precision bug: a path row fires on an NSubstitute property a test sets to a constant path
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`GitUITests.Theming.ThemeLoaderTests::Should_throw_When_cyclic_css_imports()` (`gitextensions-8522`) is a false EQ006
in P2-212's sample (row 32) that P2-218 did not clear: its path comes from
a `Returns` of a constant path on a substitute's `AppThemesDirectory` property in `CreateMockPathProvider`. P2-218 recorded the route as past its size guard: `Returns` configures the last call made on any
substitute on the thread, so showing the constant is what the property returns is a reading of every `Returns`,
`ReturnsForAll`, `When(...).Do(...)` and `Configure()` in the test assembly. It is still EQ006 at `4b2e0df6`
(P2-247, `docs/runs/2026-10-20-precision-execute-verdict-2.md`). When done, a substitute property whose only
configuration in the test assembly is `Returns` of a constant valid path is a valid path, or `equiv-adr` records why
test-double semantics stay out of scope.

## Spec references
`ValidPaths` (P2-074), ticket P2-218 (its Notes, row 32), `docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test with an NSubstitute-shaped property configured once by `Returns` of a constant path shows the path row does not
   fire; a second configuration of the same property elsewhere in the assembly keeps it.
2. `gitextensions-8522` in `full`: the test's result is given, in Notes.

## Notes
- Filed 2026-10-20 by P2-247 (criterion 8): P2-218, the cause P2-212 named, is done and left this result.
