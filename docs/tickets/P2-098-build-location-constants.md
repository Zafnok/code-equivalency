# P2-098 A constant that only says where or from which commit the code was built is not a divergence
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-058

## Goal
`equiv` compares two checkouts, so they are in two directories and usually at two commits. P2-058's
run on `gitextensions-11372` reported 25 EQ002, and 23 of them rest on a string constant that
encodes exactly that:
- 22 are calls whose `[CallerFilePath]` default argument the compiler fills with the absolute path
  of the source file. 12 of those methods are in files that are byte-identical on both sides.
- 1 (`BugReporter.Program::Main()`) reads a commit hash from a generated `ThisAssembly` class.

The same constants keep unedited bodies from being congruent. Decide, through `equiv-adr`, how such
a constant is treated, then implement it. Candidates: compare a caller-file-path argument relative
to each side's solution directory; treat `[CallerFilePath]`, `[CallerLineNumber]` and constants
from generated build-info files as one symbolic value per site on both sides; or keep the verdict
and give it its own reason so the review list ranks it last.

## Spec references
ADR 0018 (congruence), ADR 0026 (what a Divergent rests on), VERIFICATION-MODEL section 7;
`docs/runs/2026-10-02-cleanup-gitextensions-11372/SUMMARY.md` (the 23 identities).

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change. It says what happens to
   `[CallerLineNumber]`, whose value a cleanup that adds or removes a line does change.
2. A sample pair with the same body in two directories, calling a method with a
   `[CallerFilePath]` parameter, is Equivalent by congruence.
3. A pair where the caller passes a different explicit string for that parameter stays Divergent.
4. A rerun of `gitextensions-11372` has none of the 22 caller-file-path EQ002. Notes record what
   `BugReporter.Program::Main()` becomes.

## Tests
- the unit tests the decision names
- `CSharpFrontendTests.ACallerFilePathArgumentIsTheSameOnBothSides`
- `EquivalenceTests.AnExplicitDifferentPathArgumentStaysDivergent`

## Out of scope
`#line` directives. Paths that the code itself computes at run time.

## Notes
