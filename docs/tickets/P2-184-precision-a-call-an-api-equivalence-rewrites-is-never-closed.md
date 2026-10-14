# P2-184 Precision bug: a call an API-equivalence entry rewrites is never closed, so it writes the heap on the legacy side only
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-130's adjudication (`docs/runs/2026-10-14-migrations-verdict.md`) found 9 false EQ002 with one
cause, on all three migration pairs: 6 on `gitextensions-8522`, 2 on `openra-17989`, 1 on
`duplicati-3124`. Each body is unedited and calls `String.Split(params char[])` or
`String.TrimEnd(params char[])` with one character. `bcl.string-split-one-char` or
`bcl.string-trim-end-one-char` applies (`equivalencesApplied`), so both traces call the same modern
member with the same arguments. But `IrLowerer`'s rewritten call is built with `Closed: false`
(`IrLowerer.cs`, the `Rewritten` path), while the modern side's direct call of the same member gets
`ClosedCalls.IsClosed(target)`, which is true for these members (ADR 0041). So the legacy call
havocs every heap map the body touches and the modern call leaves the heap as it was; every later
call or field read then differs. P2-070 (done) added the entries and left this case.

When done, a rewritten call is closed exactly when a direct call of the modern member is.

## Spec references
ADR 0020, ADR 0041, `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs` (`Rewritten`, `Callee`),
`ClosedCalls`, `docs/tickets/done/P2-070-*.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A call rewritten by a member entry takes its closedness from the modern member, as a direct call
   of that member does.
2. A sample pair (unedited body: `Split` with one character, then a field read and a call) is
   Equivalent; one whose two bodies differ after the call is not.
3. Rerun `gitextensions-8522` and `openra-17989` in compare mode quick; Notes list the 8 procedures of
   the verdict's appendix owned by this ticket on those pairs and their new results.

## Tests
`ApiEquivalenceLoweringTests` (the rewritten call is closed for a closed modern member, not for an open
one), and the sample pair as an integration test.

## Size guard
A change to which members are closed: stop, that is ADR 0041's list.

## Out of scope
The fingerprint text of a rewritten call (P2-177).

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4). Procedures: the appendix rows owned by P2-184.
