# M2-007 `runtime-changes.json` is complete against Microsoft's breaking-change pages, and every row says where it came from
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
On a retarget, congruence decides nearly every verdict (census, 2026-09-24). A congruent pair is
Equivalent unless it calls a member in `runtime-changes.json` (ADR 0024). That table has 14 rows
picked by hand in M2-006. Any behaviour change on the .NET Framework → .NET path that the table
misses is a silent false Equivalent, on exactly the code the agent pairs produce. After this
ticket:
- every entry on Microsoft's compatibility pages for that path either names a BCL member whose
  textually identical call can behave differently, and is a row;
- or it is listed as excluded, with a reason, in a checked-in review file.

Every row states its source. No engine behaviour changes. EQ006 fires on more members, and fewer
bodies are congruent.

## Spec references
VERIFICATION-MODEL.md section 3 (runtime-changed APIs), section 6 (EQ006); ADR 0008; ADR 0024
(runtime-sensitive fingerprints); ADR 0035 (the `source` field; `measured` rows arrive in M3-033).

## Acceptance criteria (all must hold; nothing beyond them)
1. Every row in `src/Equiv.Core/RuntimeChanges/runtime-changes.json` has `source`: `curated` for
   the existing 14, `documented` for rows added here. The loader rejects a row without `source`
   or with an unknown value, and `measured` is accepted for M3-033.
2. `docs/runtime-changes-review.md` lists every entry on the Microsoft Learn pages under
   `dotnet/core/compatibility/` for .NET Core 3.0 through .NET 10, plus the .NET Framework to .NET
   porting pages, that is behavioural (not build-time or deployment). Each entry is one line:
   title, URL, then either `row: <member prefix>` or `excluded: <reason>`. The allowed reasons are
   a closed list at the top of the file: `not a BCL member`, `build-time only`, `not reachable
   from .NET Framework code`, `configuration only`, `covered by row <prefix>`.
3. Rows are added for every `row:` line. The `reason` is written in your own words, never copied,
   with the page URL. No text is quoted from the pages beyond member names.
4. `RuntimeChangeTableTests.EveryRowHasASource` and
   `RuntimeChangeTableTests.ReviewFileAndTableAgree` (every `row:` prefix exists in the table, and
   every table row appears in the review file) pass.
5. The business-layer and sample snapshots are updated for any new EQ006 or lost congruence, and
   the PR description lists each changed verdict.
6. The census on Git Extensions is not rerun here. The PR description states the new row count
   and how many of the new prefixes appear in Git Extensions' census `runtimeChangeCalls` if that
   data is at hand, or "not measured".

## Files
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`, `src/Equiv.Core/RuntimeChanges/*` (loader
validation only), `docs/runtime-changes-review.md` (new), `tests/Equiv.Core.Tests/RuntimeChanges/*`,
snapshot files that change.

## Tests
`RuntimeChangeTableTests.EveryRowHasASource`, `RuntimeChangeTableTests.UnknownSourceIsRejected`,
`RuntimeChangeTableTests.ReviewFileAndTableAgree`.

## Size guard
Code changes beyond the loader's `source` validation mean you are changing matching. Stop.

## Out of scope
Measuring anything (M3-032, M3-033). x87 floating-point rows (the table's header explains why).
Changing how prefixes match. Suppression UX.

## Notes
