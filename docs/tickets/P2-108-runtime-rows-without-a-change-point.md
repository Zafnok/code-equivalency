# P2-108 Every runtime-change row has a change point, so a .NET-to-.NET pair is not flagged for Framework differences
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066

## Goal
32 rows of `runtime-changes.json` have `changedIn: null`, which ADR 0040 decision 2 applies whenever
the two runtimes differ. P2-054 left them null because a measured witness shows only that net48 and
net10.0 differ, not where. On a .NET-to-.NET pair that rule is the largest source of false EQ006:
on P2-066's `gitextensions-9860` (net5.0 to net6.0), 56 of 59 EQ006 cite one of these rows, and none
of the 56 could be confirmed. Several of the rows record a change that is documented to predate
.NET 5, so it cannot differ between any two .NET versions:
- `System.String::Equals(string,System.StringComparison)` and its static form: ICU replaced NLS on
  Windows in .NET 5 (the table's own `IndexOf` row says `net5.0`), 12 results;
- `System.Text.Encoding::get_Default(`: UTF-8 on every .NET (Core), 6 results;
- `System.String::Split(char[],System.StringSplitOptions)` and the `string[]` form: `TrimEntries`
  exists from .NET 5, 4 results;
- `System.Math::Min`, `System.Math::Max`: IEEE 754-2019 ordering of zeros from .NET Core 3.0, 1 result.

Minimal repro, as a sample pair (identical file; legacy `net8.0`, modern `net9.0`):

```csharp
static bool Same(string a, string b) => string.Equals(a, b, System.StringComparison.CurrentCulture);
static System.Text.Encoding Enc() => System.Text.Encoding.Default;
```

Today both methods are EQ006. They should be congruent. Give every row a change point: from the
row's documentation where it names one, otherwise by running the row's witness with
`tools/runtime-diff` between adjacent runtimes (`--from net48 --to netcoreapp3.1`, then
`netcoreapp3.1` to `net5.0`, and so on to `net10.0`). A row whose witness differs at more than one
step is split into rows, one per step. A row that cannot be placed stays null, and the PR lists it
with the reason.

## Spec references
ADR 0040 decision 2 (interval, change points), ADR 0035 (measured rows), `runtime-changes.json`
header, `tools/runtime-diff/README.md`, P2-054's Notes (why the 32 stayed null),
`docs/runs/2026-10-02-upgrade-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each of the 32 rows has a `changedIn`, or is listed in `## Notes` with why it stays null. A
   documented change point cites its page; a measured one names the two runtimes whose witness
   results differ.
2. `RuntimeChangeTableTests.EveryRowHasAParseableChangedIn` caps the null count at the number left
   in `## Notes`.
3. `samples/runtime-row-framework-only-change` (the pair above) has no EQ006, and the same file with
   the legacy project on `net48` keeps both.

## Files
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`, its tests, the new sample,
`docs/runtime-changes-review.md` if a row is split.

## Tests
Named in criteria 2 and 3.

## Out of scope
Rows that already have a change point. Installing a runtime without asking the user: if a step
needs one that is not installed, say which and ask.

## Notes
- Found by P2-066 (`docs/runs/2026-10-02-upgrade-verdict.md`).
