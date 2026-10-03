# ADR 0046: A caller file path or line number the compiler supplies is an input both sides share, not a constant

Status: accepted (2026-10-03)

## Context
`equiv` compares two checkouts, so the two sides are in two directories and usually at two commits.
P2-058's run on `gitextensions-11372` reported 25 EQ002, and 23 rest on a constant that says only
that (`docs/runs/2026-10-02-cleanup-gitextensions-11372/SUMMARY.md`):
- 22 are calls to a method with a `[CallerFilePath]` parameter. The compiler fills the argument with
  the absolute path of the calling file, and the two sides were fetched into two directories. 12 of
  the 22 are in files that are byte-identical on both sides.
- 1 passes a commit hash from a `ThisAssembly` class that the build generates under `obj/`.

ADR 0024 fingerprints "constants by type and value", so the same constants also keep an unedited
body from being congruent. Across the three cleanup runs it is 45 of 52 Divergent (ROADMAP, P2-098).
`[CallerLineNumber]` has the same shape: its value changes when a line is added above the call.

## Decision
An argument the compiler supplies for a parameter marked `[CallerFilePath]` or `[CallerLineNumber]`
(Roslyn's `ArgumentKind.DefaultValue`, of type `string` or `int`) is not lowered as its constant. It
reads a synthesised input: `caller.file` for a path and `caller.line` for a line number, one of each
per body, `In`, shared by both sides by name (ADR 0021). The bound fingerprint (ADR 0024) writes the
argument as `caller=file` or `caller=line` and leaves its value out. So `equiv` does not compare
where a body sits, in which directory, file or line; VERIFICATION-MODEL section 1 says so. An
argument the source writes out is an ordinary value: a caller that passes `"a.cs"` on one side and
`"b.cs"` on the other is Divergent as before, and so is one that passes an explicit value against
the other side's supplied one. The IL lowering (ADR 0039) cannot see which arguments the compiler
supplied, so it applies the rule to a string constant equal to the body's own file path, and to an
`int` constant inside the body's own line span, passed for such a parameter. A constant read from
a generated build-information class (`ThisAssembly.Git.Sha`) stays an ordinary constant.

## Why
- The value is not in the source text. Two byte-identical files give two different strings only
  because of where they were checked out, and the same file gives a different line number only
  because something above it moved. A reviewer of a cleanup or a migration does not count either as
  a change in behaviour, and a gate that fails on it gets switched off (ADR 0026's argument).
- A shared input is exact about what remains. Both sides read the same unknown value, so the proof
  holds for every path and line, and nothing in the body can depend on the value in a way that
  differs between the sides without that being reported.
- One `caller.line` per body, not one per call site. Numbering sites by position gives a false
  Divergent when an edit reorders two sites (an inverted `if`), and the real line numbers differ
  there for the same reason they differ after an inserted line. One input says the rule plainly:
  line numbers are not compared.
- It uses what exists: a synthesised input shared by name, as `typeof.<T>` is. No IR instruction,
  no Core contract, no SARIF property and no rule id changes.
- `[CallerMemberName]` and `[CallerArgumentExpression]` are left alone. Their values are source text
  (the member's name, the argument's text), the same in any directory and on any line.

## Rejected
- **The path relative to each side's solution directory.** It keeps a moved or renamed file visible,
  but it needs the solution root in the lowerer and the fingerprint, a rule for a file outside that
  root and for a project given without a solution, and it lowers the path to a string that an
  explicit literal on the other side can equal by accident. It also leaves the line number unsolved.
- **One `caller.line.<k>` per call site.** See Why: reordering two sites becomes a false Divergent.
- **Keep the constant, and give the Divergent its own reason so the review list ranks it last.** It
  changes the SARIF shape, still fails a `--fail-on divergent` gate, and still keeps byte-identical
  bodies out of congruence, which is most of the cost.
- **The generated commit constant as a shared input too.** Nothing in the bound tree separates a
  constant the build wrote from one a source generator, a T4 template or a resource designer wrote,
  and those are program text: a changed protocol version in a generated file is a real change.
  The difference is also real (the program reports another commit), there is one per run on a
  repository that uses such a package, and it is the same result for any two commits.
- **Name the assumption on each result that leans on it.** No section 1 assumption other than
  `assumedCallees` is marked per result (ADR 0044's reason).

## Consequences
- A body that only calls such a method is congruent again when nothing else changed. A false
  Equivalent is now possible in the sense section 1 names: a pair whose only difference is the path
  or line the compiler supplies (a moved file, a shifted line) is Equivalent.
- An explicit line number against a supplied one is Divergent even if the two numbers happen to be
  equal in this build. That is right for every build after the next edit above the call.
- In the IL lowering, an explicit literal that equals the body's own path, or an explicit line
  inside the body's own span, is taken as supplied. An opaque IL fragment still hashes the literal,
  so a fragment holding such a call is unshared and Unknown, never Divergent.
- A model that fixes `caller.file` or `caller.line` has a synthesised input that `--execute`'s
  replay cannot set, so the replay is unavailable for it (section 6, unchanged).
- `BugReporter.Program::Main()` stays Divergent on any pair of two commits. P2-098's Notes record it.
- VERIFICATION-MODEL section 1 gains the statement in this PR. Sections 2 and 3, the fingerprint and
  both lowerings change in P2-098's implementation PR, with
  `CSharpFrontendTests.ACallerFilePathArgumentIsTheSameOnBothSides`,
  `CSharpFrontendTests.ACallerLineNumberArgumentIsTheSameOnBothSides`,
  `CSharpFrontendTests.AnExplicitCallerArgumentIsAnOrdinaryValue`,
  `IlLowererTests.ACallerLocationConstantOfTheBodyIsTheSharedInput` and
  `CallerLocationEquivalenceTests.AnExplicitDifferentPathArgumentStaysDivergent`.
