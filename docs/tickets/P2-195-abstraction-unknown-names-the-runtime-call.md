# P2-195 An `abstraction` Unknown on a body a runtime rule keeps out of congruence names the runtime-changed call its candidate starts from
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-181's count (equiv `c6d5f751`, compare mode quick, the three migration pairs; its Notes) found 348
`abstraction` Unknowns among the 1,004 unedited bodies a runtime rule keeps out of congruence. In 347
of them, every entry of `properties.abstractions` is a function both sides share: `delegate` (130),
`String.Concat(string,string)` (68), `String.IsNullOrEmpty` (51), `String.Length` (37) and others. None
is the runtime-changed member. The two texts are equal, so the shared function can differ only because
its arguments differ, and they differ only because a runtime-changed call gave each side its own
result. The reviewer is told that the result depends on `String.Concat`, and the member that is the
cause is named only in `notCongruent`.

When done, such an Unknown says which runtime-changed call its candidate's two sides first differ at,
and the reason stays `abstraction`: whether the change reaches the output is still not decided.

## Spec references
ADR 0026 and ADR 0053 (abstractions), ADR 0040, VERIFICATION-MODEL section 6 (`abstractions`,
`notCongruent`), `docs/tickets/done/P2-181-*.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. An `abstraction` Unknown whose model has a runtime-changed call with different results on the two
   sides names that call's member in the result, as an entry `properties.abstractions` can hold or as a
   property beside it, decided with `equiv-decide` and documented in VERIFICATION-MODEL section 6.
2. Rerun the three migration pairs in compare mode quick; Notes give the members named, top 15.

## Tests
A unit test with a runtime-changed call feeding a shared function; a snapshot of the SARIF result.

## Size guard
Changing a verdict: stop. This is a report, not a decision.

## Out of scope
Deciding the 348 (P2-181 decided what its rules could).

## Notes
- Filed 2026-10-14 by P2-181 (criterion 1).
