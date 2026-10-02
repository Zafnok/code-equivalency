# P2-084 A project whose only types are empty is not a load failure
Status: todo
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
   empty. Its existing test stays green unchanged.
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
