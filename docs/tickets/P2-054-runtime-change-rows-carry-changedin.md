# P2-054 Every runtime-change row says which runtime changed it
Status: todo
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
