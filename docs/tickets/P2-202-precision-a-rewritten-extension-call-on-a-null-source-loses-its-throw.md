# P2-202 Precision bug: a rewritten extension call on a null source passes the null to a closed member, so the legacy side's ArgumentNullException is lost
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-184

## Goal
`bcl.string-contains-char` rewrites `Enumerable.Contains<char>(s, c)` to `String.Contains(char)`. Its
reason says the entry covers only a non-null `s`: on null the legacy call throws
`ArgumentNullException` and the modern side's receiver check throws `NullReferenceException`, "so that
pair stays Divergent on null". `IrLowerer.Adapt` null-checks the receiver of an instance call only; a
static or extension legacy call has no check, so on a null `s` the rewritten call is made with a null
receiver. Since P2-184 that call is closed, as the modern side's direct call is, and the replay taints
what the solver chooses for it (ADR 0056). So `samples/api-drift`'s `Text.HasX(string)`, which was
Divergent on a null `s`, is Unknown(Abstraction) naming `String.Contains(char)`, equivalent when
`s != null`.

When done, a rewritten call whose legacy member throws `ArgumentNullException` for a null source
argument throws it before the modern call, and `HasX` is Divergent again.

## Spec references
ADR 0020, ADR 0056, `src/Equiv.Core/ApiEquivalences/api-equivalences.json` (`bcl.string-contains-char`),
`IrLowerer.Adapt`, `docs/tickets/done/P2-184-*.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A catalogue entry can say which source arguments the legacy member checks for null, and the
   rewritten call throws `System.ArgumentNullException` on a null one, before the modern call.
2. `bcl.string-contains-char` says so for its source; its reason matches what the lowering does.
3. `api-drift`'s `HasX` is EQ002 with `System.ArgumentNullException` on the legacy side, and its
   README and `expected.sarif.json` say so.

## Tests
`ApiEquivalenceLoweringTests` (a null source throws, a non-null one calls the modern member), the
catalogue schema test for the new field, and `SamplesEndToEndTests` for `HasX`.

## Size guard
An entry whose legacy member checks anything other than null: stop, that is not this ticket.

## Out of scope
The closed-call taint rule (ADR 0056). P2-203's closed members that differ in their answers.

## Notes
- Filed 2026-10-15 by P2-184, whose fix closed the rewritten call (the `HasX` sample result).
