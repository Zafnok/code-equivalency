# P2-219 Precision: "always throws on .NET" rows for members that .NET Core 3.0 and later implement
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`runtime-changes.json` takes the "Unsupported APIs" page (`docs/core/compatibility/unsupported-apis`) as rows with
`changedIn` `netcoreapp1.0` and the reason "Always throws (usually PlatformNotSupportedException) on .NET". The page
was written for .NET Core 1.x and 2.x. Some of its members were implemented later:
`System.Security.Cryptography.RSA::FromXmlString(` and `::ToXmlString(` work since .NET Core 3.0 (the API page's
source link is `RSA.Xml.cs`), so on a pair from .NET Framework to .NET 5 a call to them does not throw on either side.

P2-212's adjudication (`docs/runs/2026-10-15-precision-verdict.md`) found 1 false EQ006 from this, in its fresh
sample of `openra-17989`: `OpenRA.LocalPlayerProfile::.ctor(string,OpenRA.PlayerDatabase)`, under the
`RSA::FromXmlString(` row, whose counterexample has the modern side throw. The same profile type calls
`ToXmlString` in another method.

When done, each row from that page says the versions in which the member throws, and a pair whose modern runtime
implements the member does not fire it.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (the rows citing `unsupported-apis`),
`docs/runtime-changes-review.md`, ADR 0040 (rows and intervals), `.claude/skills/equiv-adr` (a row that changes back
needs an end to its interval, which a row may not express yet).

## Acceptance criteria (all must hold; nothing beyond them)
1. Every row that cites the unsupported-APIs page is checked against the member's API page for the versions that
   implement it; Notes list each one checked and the result.
2. A row whose member is implemented from some version on applies only to a pair whose modern runtime is before it
   (an end on the row's interval, through `equiv-adr` if the table cannot express one), or is removed with its
   reason in the review file.
3. A sample pair from `net472` to `net5.0` calling `RSA.FromXmlString` is congruent; `openra-17989` rerun in compare
   mode quick, with `LocalPlayerProfile`'s new result in Notes.

## Tests
The runtime-change table tests; the sample.

## Out of scope
Rows from other pages.

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6).
