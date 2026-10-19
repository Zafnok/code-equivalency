# P2-253: Git Extensions' replays and `probe` on `Microsoft.WindowsDesktop.App` 5.0 (2026-10-19)

`gitextensions-8522` in `full` mode with `--execute`, compare mode quick (ADR 0052), `--jobs 4`, at equiv `ff162425`
(branch `P2-253-windows-desktop-replay-driver`), from one Release build, on the checkout and restore P2-237 made; then
`probe` (`equiv mcp --execute`) on P2-245's inputs for P2-212 sample rows 22 and 41 and on the three `AppSettings`
members. Wall-clock 353 s, exit 1 (Divergent results exist), against P2-245's 338 s at `bceeea62`
(`docs/runs/2026-10-19-full-execute-gitextensions-8522/SUMMARY.md`). Only the drivers changed between the two runs, so
everything here is set beside P2-245's run and its adjudication (`docs/runs/2026-10-19-execution-reach.md`).

**This pair takes no part in ADR 0028's rule table here.** This file measures and adjudicates; it has no continue,
re-scope or stop line.

## The runtime

The box has `Microsoft.WindowsDesktop.App` 10.x only, and .NET 5 has been out of support since 2022-05-10, so it was
not installed machine-wide: an unserviced runtime left in `C:\Program Files\dotnet` would run any `net5.0-windows`
application on this box. Instead, a private dotnet root in the session's scratch folder held a copy of `dotnet.exe`,
junctions to the machine install's `host`, `sdk`, `packs`, `sdk-manifests`, `templates`, `metadata`,
`shared/Microsoft.NETCore.App`, `shared/Microsoft.AspNetCore.App` and its two `shared/Microsoft.WindowsDesktop.App`
versions, and `shared/Microsoft.WindowsDesktop.App/5.0.17` from Microsoft's
`windowsdesktop-runtime-5.0.17-win-x64.zip` (`builds.dotnet.microsoft.com`; the 5.0 release metadata lists no hash
for the zip, so each of its 59 DLLs was checked instead: every one has a valid Authenticode signature by Microsoft
Corporation). `equiv` and its drivers ran with that root first on `PATH` and as `DOTNET_ROOT`, so
`DriverReferences.Installed()` found desktop 5.0.17 beside `Microsoft.NETCore.App` 5.0.17. The machine install is
unchanged, and the private root is deleted with the session.

## Replay

A modern Git Extensions project that compiles against the Windows Desktop pack (`net5.0-windows` with Windows Forms) now
has its replay drivers run on `Microsoft.WindowsDesktop.App` 5.0.17; the legacy side is unchanged (.NET Framework 4.8).

| Rule | Replay | P2-245 | Here |
|---|---|---|---|
| EQ002 | `not-constructible` | 24 | 24 |
| EQ006 | `reproduced` | 8 | 7 |
| EQ006 | `not-constructible` | 10 | 10 |
| EQ007 | `not-applicable` | 10 | 12 |
| EQ007 | `not-constructible` | 93 | 92 |

No replay is `not-reproduced`. The five replays whose P2-245 result came from a Windows Desktop assembly the modern
driver could not load now run:

| Procedure | P2-245 | Here |
|---|---|---|
| `GitCommands.AppSettings::ReadBoolRegKey(string,bool)` | EQ006 `reproduced` (the modern `AppSettings` static constructor threw `TypeInitializationException`) | EQ007 `not-applicable`: both returned `false` |
| `ICSharpCode.TextEditor.TextAreaMouseHandler::IsSelectableChar(char)` | EQ007 `not-constructible` (modern `FileNotFoundException`) | EQ007 `not-applicable`: both returned `false` |
| `ICSharpCode.TextEditor.TextEditorControlBase::ParseFont(string)` | EQ007 `not-constructible` (modern `FileNotFoundException`) | EQ007 `not-constructible`: the call traces split before the outcome |
| `ICSharpCode.TextEditor.Document.FontContainer::ParseFont(string)` | as above | as above |
| `GitUI.CommandsDialogs.FormSparseWorkingCopyViewModel::GetPathToSparseCheckoutFile()` | EQ007 `not-constructible` (modern `TypeInitializationException`) | EQ007 `not-constructible`: the call traces split before the outcome |

`ReadBoolRegKey` was EQ006 only because its replay was `reproduced` (ADR 0060 decision 4); with both sides returning
alike it is the EQ007 the runtime-change exposure makes it. `AppSettings::GetDictionaryDir()` and
`::GetGitExtensionsDirectory()` are still `reproduced`, but no longer for P2-253's reason: a `probe` of each returns a
path under the legacy driver's own folder (`<root>\legacy\GitCommands`) on one side and the private `dotnet` root on the
other, because `Application.ExecutablePath` is the process's executable, which is the driver on .NET Framework and the
`dotnet` host on .NET. That is the harness again, as P2-254 is for the working directory, and P2-254 does not fix it:
**P2-255** (new) owns it. Neither is in P2-212's sample.

## Rows 22 and 41

P2-237's method, as P2-245 applied it: **confirmed** needs a `probe` run on both runtimes whose outcomes differ for
the claimed reason. Inputs are P2-245's, unchanged; each case ran under the invariant culture.

| Row | Procedure | Rule | Probe | P2-245 | Here |
|---|---|---|---|---|---|
| 22 | `GitUI.CommitInfo.CommitInfo.BranchComparer::Compare(string,string)` | EQ002 | 22-a (`"a"`, `"B"`, receiver by `BranchComparer(string)`): legacy returned -1, modern 31; 22-b (`"é"`, `"f"`): -1 against 131 | undetermined (not callable) | **confirmed**: the legacy side compares with `Comparer<string>.Default`, culture-aware, the modern with `StringComparer.Ordinal`, the code change the result claims |
| 41 | `GitUI.CommandsDialogs.FormVerify::GuessFileTypeWithContent(string)` | EQ006 | 41-c (`"\u0000{}"`): legacy `recovery.txt`, modern `recovery.json`; 41-d (`"\u0000<html>"`): `recovery.txt` against `recovery.html`; 41-e (`"\u200B{}"`): `recovery.txt` against `recovery.json` | undetermined (not callable) | **confirmed**: ICU ignores the leading NUL or zero-width space in the culture-sensitive `StartsWith`, NLS does not, the runtime change the result claims |

Both members are reached as P2-245 reached them (a nested type of a `UserControl`, a static member of a `Form`); no
window is created, so ADR 0035's desktop rule is untouched.

## Re-adjudication and precision

P2-245's table with these two rows moved from undetermined to confirmed; nothing else changes.

| Pair | Rule | Rows | Confirmed | False positive | Still undetermined |
|---|---|---|---|---|---|
| gitextensions-8522 | EQ002 | 12 | 1 | 6 | 5 |
| | EQ006 | 12 | 7 | 0 | 5 |
| **All three pairs** | **all** | **51** | **24** | **7** | **20** |

| Pair | Rule | P2-245 | Here |
|---|---|---|---|
| gitextensions-8522 | EQ002 | 13.3% (2 of 15) | 18.8% (3 of 16) |
| | EQ006 | 56.3% (9 of 16) | 58.8% (10 of 17) |
| | **all** | **35.5% (11 of 31)** | **39.4% (13 of 33)** |
| **Migrations** | EQ002 | 8.7% (2 of 23) | 12.5% (3 of 24) |
| | EQ006 | 68.5% (37 of 54) | 69.1% (38 of 55) |
| | **all** | **50.6% (39 of 77)** | **51.9% (41 of 79)** |

The ceiling (every undetermined result real) stays 68.1% (81 of 119): two undetermined results became confirmed.
Of P2-245's five not-callable rows, three are left: Git Extensions 13 (`ExCSS.StyleRule`), 16 (an `IWin32Window`
argument) and 45 (a window receiver), the last two by ADR 0035's desktop rule.

- Decision: the scoreboard is not changed here, as P2-245 decided for its own re-adjudication: its migration row is
  P2-237's measurement, and P2-247 is the re-adjudication that refreshes it. The 51.9% stands here beside P2-245's 50.6%.
