# P2-084 A project whose only types are empty is not a load failure
Status: done (PR #331)
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-065's run of the human pair `duplicati-3124` has a project load rate of 98.1%: `Duplicati.Tools`
is skipped on both sides with "the project loaded and declares at least one type, but symbol
enumeration found zero procedures". ADR 0028 makes a load rate below 100% on a human pair a ticket.

The project is a placeholder: one source file with one empty class, there so the build accepts the
project's content files. It has no method on either side, so nothing is missing from the comparison.
P2-018's rule (`CSharpFrontend.SkipVacuousProjects`) exists to catch a project whose methods were
lost, where the source has bodies and enumeration found none. An empty class is not that case.
Tell the two apart: a project is vacuous only when its syntax declares at least one member with a
body (or an expression body, or an accessor) and enumeration still found no procedure.

## Spec references
P2-018 (the rule and why it exists), ADR 0029 (a skipped project counts against the load rate),
ADR 0028 (the load-rate rule), `src/Equiv.Frontend.CSharp/CSharpFrontend.cs`
(`SkipVacuousProjects`, `DeclaresAType`), `docs/runs/2026-10-01-full-duplicati-3124/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A project whose only type declarations have no member with a body loads, is not in
   `projectsSkipped`, and raises no notification. Unit test with one empty class.
2. P2-018's case still skips: a project whose syntax has a method body and whose enumeration is
   empty. Its existing tests stay green with their assertions unchanged; their fixture gains a
   destructor (see the `Deviation:` in Notes).
3. A `--lower-only` run of `duplicati-3124` reports `projectsSkipped` 0 on both sides. The figure
   goes in `## Notes`.

## Files
`src/Equiv.Frontend.CSharp/CSharpFrontend.cs`, `tests/Equiv.Frontend.CSharp.Tests`.

## Tests
Named in criteria 1 and 2.

## Size guard
If telling the cases apart needs more than a syntax walk over the compilation's trees, stop and
write what was found in `## Notes`.

## Out of scope
Any other skip reason. How the load rate is computed.

## Notes
- Found by P2-065, at equiv ef79ff6.
- Deviation: criterion 2 said P2-018's existing test stays green unchanged. It could not: the fixture in
  `ProjectWithTypesButNoProcedures_IsSkipped`, `AVacuousProjectIsSkippedWhateverItsOtherTreesHoldAndItsNeighboursStay`
  and `NoProcedures_ExitsFour` was `class Empty { public int X; }`, a field and no body, which is exactly the
  kind of project this ticket says is not a load failure. The fixture now also declares `~Empty() { }`, a body
  the enumerator does not count (destructors are not procedures). Every assertion in the three tests is unchanged.
- Decision: the walk is restricted to nodes inside a type declaration, so a project of top-level statements
  only (no type) stays unaffected, as it was under `DeclaresAType`. Simpler alternative (any body anywhere in
  the tree) would have newly skipped such a project whenever it holds an expression-bodied local function.
- Decision: "a body" is an `AccessorDeclarationSyntax`, an `ArrowExpressionClauseSyntax`, or a
  `BaseMethodDeclarationSyntax` with a block. The notification text is unchanged; it is still true.
- Observed, not changed (the ticket counts any accessor): an interface-only project that declares a property
  (`int P { get; }`) has an accessor in syntax and no procedure, so it is still skipped, as before this ticket.
  A contracts-only project with no property now loads. No corpus pair shows the first case yet.
- Criterion 3: `--lower-only` run of `duplicati-3124` at equiv e2741c6, 2026-10-01: `projectsSkipped` legacy 0,
  modern 0 (was 1 and 1). `Duplicati.Tools` logs `outcome=loaded` on both sides; load-legacy 51 of 51,
  load-modern 52 of 52, so the project load rate is 100% (103 of 103). Procedures 6340 / 6367 and matched pairs
  6275 are the same as P2-065's run. Exit 5, 99 s: the eight pair-level lowering crashes P2-083 owns.
