# P2-098 A constant that only says where or from which commit the code was built is not a divergence
Status: done (PR #367)
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
- Decision (criterion 1, `equiv-adr` bar test, 2026-10-03): a new ADR, 0046, accepted. It changes what
  ADR 0024 fingerprints ("constants by type and value") and what an Equivalent claims, so it is the
  bar test's last row and not a clarification. A supplied `[CallerFilePath]` or `[CallerLineNumber]`
  argument reads a synthesised input, `caller.file` or `caller.line`, shared by name. A line number is
  therefore not compared: one input per body, not per site. The generated commit constant stays an
  ordinary constant. Alternatives: the path relative to the solution directory; one line input per
  call site; keep the Divergent under its own reason. Rule: `equiv-adr` bar test, last row.
- Deviation: criterion 1 says the outcome is merged before any code change. The ADR is written as
  accepted, in its own PR with VERIFICATION-MODEL section 1, as ADR 0044 was for P2-102: the PR's
  merge is the acceptance. The number is 0046 because open PRs already hold 0043 and 0045.
- Decision: where `AnExplicitDifferentPathArgumentStaysDivergent` lives -> a new
  `CallerLocationEquivalenceTests` in `Equiv.Tests.Integration`. Alternatives: a class named
  `EquivalenceTests`, which does not exist; every such class there is `<Topic>EquivalenceTests`. Rule: 4.
- Criterion 4, the rerun (2026-10-03, `gitextensions-11372`, mode full, equiv 77194ce, wall-clock 2455s,
  exit 5 from the one lowering crash P2-105 already owns). EQ002 went from 25 to 3. All 22
  caller-file-path identities listed in `docs/runs/2026-10-02-cleanup-gitextensions-11372/SUMMARY.md`
  are now EQ001 with `proofMethod: congruence`. By rule: EQ001 14249 (congruence 14242, bounded 7),
  EQ002 3, EQ003 330. Congruent pairs rose from 14208 to 14242.
- `BugReporter.Program::Main()` stays EQ002, as ADR 0046 decides: it passes the generated commit
  constant, and the two sides are two commits. The other two EQ002 are the collection-expression
  pairs P2-099 owns (`RepositoryXmlSerialiserTests::Serialize_recent_repositories()` and
  `RepositoryCategorySerialiserTests::Verify_backwards_compatibility_of_object_graph()`); they no
  longer rest on the caller file path as well.
- The rerun took 41 minutes where P2-058 took 4h43m. That is P2-076 (merged since) and a box not
  shared with another run, not this ticket.
- Not covered: an opaque IL fragment hashes its ILAst text, literal included, so under the IL
  fallback a fragment that holds such a call is unshared and Unknown (ADR 0046, Consequences).
- No SUMMARY.md was written for the rerun: the criterion asks for these Notes only, and the
  2026-10-02 summary stays the record of the run that found the problem.
