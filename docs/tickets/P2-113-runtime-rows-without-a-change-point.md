# P2-113 Every runtime-change row has a change point, so a .NET-to-.NET pair is not flagged for Framework differences
Status: in-progress
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
`docs/runs/2026-10-03-upgrade-verdict.md`.

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
- Found by P2-066 (`docs/runs/2026-10-03-upgrade-verdict.md`).
- On `jellyfin-13023` (net8.0 to net9.0) all 102 EQ006 cite one of these rows, 92 of them the two
  `String.Equals` rows, and 353 unedited bodies that hold such a call are not congruent. Over both
  pairs: 158 of 161 EQ006, 120 adjudicated false and 38 undetermined.
- Result: all 32 rows have a `changedIn`; none stays null, so the cap in
  `EveryRowHasAParseableChangedIn` is 0. The table has 429 rows (two rows were split).
- Documented (6 rows; each row's `url` is the page that names the version):
  - `String.Equals(string,StringComparison)` and its static form: `net5.0`, the ICU page the `IndexOf` row cites.
  - `String.Split(char[],StringSplitOptions)` and the `string[]` form: `net5.0`. The `StringSplitOptions` page says
    `TrimEntries` "is available in .NET 5 and later versions only".
  - `Encoding.Default`: `netcoreapp1.0`. Its API page says .NET Core and later always return UTF-8.
  - `Size.Ceiling(SizeF)`: `net9.0`, the float-to-integer saturation page (ADR 0040 names the same change point).
- Measured (26 rows): `tools/runtime-diff` at seed 0, 64 cases, between each adjacent pair of net48, netcoreapp3.0,
  netcoreapp3.1, net5.0, net6.0, net7.0, net8.0, net9.0 and net10.0. The two runtimes whose results differ:
  - net48 and netcoreapp3.0 (`netcoreapp1.0`, 15 rows): `String.GetHashCode(` (deterministic on net48, different in
    every process on netcoreapp3.0; runtime-diff counts that as nondeterministic on one side, not as divergent),
    `Font.FromLogFont(object)`, `RectangleF.Union`, `Environment.GetEnvironmentVariable(string,EnvironmentVariableTarget)`,
    `Environment.GetFolderPath(SpecialFolder)`, `IsolatedStorageFileStream..ctor(string,FileMode,FileAccess,FileShare)`,
    `Math.Ceiling(double)`, `Math.Cos(double)`, `Math.Sin(double)`, `Math.Max` and `Math.Min` (`double` and `float`),
    `WebUtility.HtmlEncode(string)`, `String.ToUpper(CultureInfo)`. None differs between netcoreapp3.0 and netcoreapp3.1.
  - netcoreapp3.1 and net5.0 (`net5.0`): `Char.IsLetterOrDigit(char)`, `Uri.EscapeDataString(string)` (lone surrogate).
  - net5.0 and net6.0 (`net6.0`): `String.Remove(int)`.
  - net6.0 and net7.0 (`net7.0`): `File.ReadAllText(string,Encoding)`, `File.WriteAllLines(string,IEnumerable<string>)`,
    `File.WriteAllText(string,string,Encoding)`, `StreamReader..ctor(string,Encoding)`, `ButtonRenderer.DrawButton`.
  - net7.0 and net8.0 (`net8.0`): `AuthenticationHeaderValue..ctor(string,string)`,
    `MediaTypeWithQualityHeaderValue..ctor(string)`.
  - net8.0 and net9.0 (`net9.0`): `File.WriteAllBytes(string,byte[])`.
- Split (2 rows became 4; `docs/runtime-changes-review.md` lists each):
  - `StreamReader..ctor(string,Encoding)` also differs between net9.0 and net10.0: a null encoding is accepted from
    .NET 10 (`["i", null]`: ArgumentNullException, then FileNotFoundException). Rows at `net7.0` and `net10.0`.
  - `Uri.EscapeDataString(string)` also differs between net48 and netcoreapp3.0: an apostrophe is escaped as `%27` on
    .NET. Rows at `netcoreapp1.0` and `net5.0`.
- Decision: a row measured to differ between net48 and netcoreapp3.0 gets `netcoreapp1.0`, the table's marker for the
  .NET Framework to .NET boundary, as P2-054's measured path-validation rows have. The measurement cannot tell 1.0 from
  3.0, and no supported pair can either: `coveredFrom` is netcoreapp3.0.
- Decision: split rows share a `member`, so `EveryRowHasASource` now requires distinct (`member`, `changedIn`) pairs.
  `TryMatch` needed no change: it returns the first row the pair crosses.
- Decision: the net48 variant of the sample is `samples/runtime-row-framework-only-change/net48/legacy/`, an old-style
  project that compiles `legacy/Text.cs`. The folder is named `legacy` so that `build.ps1` and `parity-run.ps1` restore
  it with MSBuild as they do every non-SDK legacy side.
- Runtimes: only net48, .NET 6 (no reference pack) and .NET 10 were installed. The user approved installing the rest.
  The session was not elevated, so the 3.0, 3.1, 5.0, 6.0, 7.0, 8.0, 9.0 and 10.0 SDKs went to the per-user location
  `%LOCALAPPDATA%\Microsoft\dotnet` with Microsoft's `dotnet-install.ps1`, and runtime-diff ran on that `dotnet`
  (`DOTNET_ROOT` set to it), because it looks for runtimes in the install it runs on.
- A fresh worktree fails `version-bump` and this sample until `dotnet restore` has run on their SDK-style projects;
  `build.ps1 -Integration` does that.
