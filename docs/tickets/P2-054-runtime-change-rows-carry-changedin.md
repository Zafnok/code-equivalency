# P2-054 Every runtime-change row says which runtime changed it
Status: in-progress
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-053

## Goal
`runtime-changes.json` has 367 rows and none says when its behaviour changed. The version is only in
the free-text `reason` and in the URL path (`compatibility/3.0` ×24, `5.0` ×56, `6.0` ×45, `7.0` ×42,
`8.0` ×41, `9.0` ×14, `10.0` ×13, `unsupported-apis` ×117, `fx-core` ×8). Add `changedIn` to every row
and let `RuntimeChangeTable` answer "does this row apply between these two runtimes?"
(ADR 0040 decision 2). No caller changes yet; P2-055 switches them over.

## Spec references
ADR 0040 decision 2; ADR 0036 (a table row is a hypothesis until a checker admits it);
`docs/runtime-changes-review.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `RuntimeChange` gains `ChangedIn`, a `TargetRuntime`, or null for "unknown".
   - `Parse` reads `changedIn` as a target framework moniker, and rejects a malformed one with the
     row's member in the message.
   - The top-level JSON gains `coveredFrom` (`netcoreapp3.0`), the oldest .NET version whose
     changes the table lists.
2. Every row has `changedIn`, filled once by a script checked in under `tools/` and not run in CI:
   - a `compatibility/<v>` URL gets `net<v>` (`netcoreapp3.0` for 3.0);
   - `unsupported-apis`, `fx-core` and the three measured rows get `netcoreapp1.0`, the Framework to
     .NET boundary;
   - a curated row gets the version its `reason` names;
   - rows the script cannot place stay null and are listed in the PR.
3. `RuntimeChangeTable.TryMatch(identity, RuntimeInterval)` applies a row only if its `changedIn`
   lies in the interval (ADR 0040). A null `changedIn` applies whenever the interval is non-empty. The
   old overload stays until P2-055 removes it.
4. `RuntimeInterval` in Core is built from two `TargetRuntime`s in either order. `Crosses(TargetRuntime)`
   is half-open, `(older, newer]`, and `IsEmpty` holds for equal runtimes. `UncoveredRange(coveredFrom)`
   returns the part of the interval the table does not cover (for example `netcoreapp2.1`–`netcoreapp3.0`),
   or null.
5. `runtime-changes.json`'s header says the rows record changes between the runtimes named by
   `changedIn`, not "between .NET Framework 4.8 and .NET 10".

## Files
`src/Equiv.Core/RuntimeChanges/RuntimeChange.cs`, `RuntimeChangeTable.cs`, `runtime-changes.json`,
`src/Equiv.Core/RuntimeInterval.cs` (new), `tools/runtime-changes-backfill/**` (new), tests.

## Tests
`RuntimeChangeTableTests.EveryRowHasAParseableChangedIn` (null counted and capped at the PR's list),
`RuntimeChangeTableTests.RowAppliesOnlyInsideTheInterval`, `RuntimeChangeTableTests.UnknownChangePointAppliesWhenRuntimesDiffer`,
`RuntimeChangeTableTests.SameRuntimeMatchesNothing`, `RuntimeIntervalTests.FrameworkToCoreCrossesTheBoundary`,
`RuntimeIntervalTests.OrderDoesNotMatter`, `RuntimeIntervalTests.ReportsTheUncoveredRange`.

## Size guard
A caller of `TryMatch` changing in `src/Equiv.Frontend.CSharp` or `src/Equiv.Verify.Z3` belongs to P2-055. Any
hand edit to more than 20 rows means the script is wrong: fix the script.

## Out of scope
New rows. Measuring change points with `runtime-diff` (P2-056 makes that possible).

## Notes
- Decision: the root of `runtime-changes.json` becomes `{ "coveredFrom": ..., "rows": [...] }`; a row must have `changedIn` (null allowed), and a missing one is rejected like a missing `source`, so `EveryRowHasAParseableChangedIn` cannot pass on an omitted field.
- Decision: the backfill is a Windows PowerShell 5.1 script, like `tools/z3-feed/fetch.ps1`. It inserts one line per row and indents the array, so the diff stays reviewable, and it refuses to run twice.
- Decision: `UncoveredRange` treats the .NET Framework to .NET boundary as covered, because the `netcoreapp1.0` rows (fx-core, unsupported-apis) cover it. Only a .NET (Core) side older than `coveredFrom` leaves a gap. Otherwise every Framework pair in the corpus would get the run-level notification.
- Decision: a `RuntimeInterval` overload with suppression, `TryMatch(identity, interval, suppressed, out match)`, sits beside the ticket's three-argument one. The frontend's flagging needs suppression, so P2-055 can switch callers without touching Core again.
- Deviation: the ticket was written against 367 rows with three measured ones. The table now has 426 rows, 62 of them measured (runtime-diff runs after the ticket was written). A measured witness proves only that net48 and net10.0 differ, not where the behaviour changed, and at least one measured row changed after the boundary: `Size::Ceiling` float-to-int saturation, at net9.0 per ADR 0040. So only measured rows whose reason is .NET Framework's upfront path-character validation get `netcoreapp1.0`, as the ticket's three rows do. The other 30 measured rows stay null, which applies whenever the runtimes differ. That is sound, and P2-056 can measure the real point.
- Null `changedIn` (32 rows, the cap in `EveryRowHasAParseableChangedIn`): curated `System.String::GetHashCode(` and `System.Text.Encoding::get_Default(` (their reasons name no version and their URLs are API pages), plus the 30 measured rows above.
- The header text now says rows record changes at the runtime `changedIn` names (criterion 5). `docs/runtime-changes-review.md` needed no change: it keys on `member`.
