# P2-251: the replays whose model holds an enum (2026-10-19)

`gitextensions-8522` and `duplicati-3124` in `full` mode with `--execute`, compare mode quick (ADR 0052), `--jobs 4`,
at equiv `ee6b15a7` (branch `P2-251-replay-enum-sort-element`), from one Release build, on the checkouts and restores
P2-237 made. `openra-17989` holds none of the six replays, so it was not rerun. Git Extensions ran twice: once on the
machine's runtimes (398 s, exit 1), where every modern driver of a `net5.0-windows` project is `not-constructible`
("runtime net5.0-windows not installed", P2-253), and once (392 s, exit 1) with P2-253's private `dotnet` root first on
`PATH` and as `DOTNET_ROOT`, the root `docs/runs/2026-10-19-desktop-replay.md` describes, still present in that
session's scratch folder and only read here. The values below are from the second run. Duplicati ran once (661 s,
exit 1).

**This takes no part in ADR 0028's rule table.** This file measures; it has no continue, re-scope or stop line.

## The six replays

P2-245's runs (`docs/runs/2026-10-19-execution-reach.md`) left six replays `not-constructible` because an argument was an
enum, which the model holds as a sort element, or a `System.Drawing.Color`. With P2-251 an enum element that is a
member's constant element is that member, and any other element a value no member of either side has.

| Pair | Procedure | Before | Here |
|---|---|---|---|
| gitextensions-8522 | `GitCommands.EnvironmentAbstraction::GetEnvironmentVariable(string,System.EnvironmentVariableTarget)` | EQ007 `not-constructible` (no `EnvironmentVariableTarget` argument) | EQ006 `reproduced` |
| gitextensions-8522 | `GitCommands.EnvironmentAbstraction::GetFolderPath(System.Environment.SpecialFolder)` | EQ007 `not-constructible` (no `SpecialFolder` argument) | EQ006 `reproduced` |
| gitextensions-8522 | `GitCommands.Settings.ConfigFileSettings::CreateLocal(GitCommands.GitModule,GitCommands.Settings.ConfigFileSettings,GitUIPluginInterfaces.SettingLevel,bool)` | EQ007 `not-constructible` (no `SettingLevel` argument) | EQ007 `not-constructible`: the call traces split before the outcome |
| gitextensions-8522 | `System.StringExtensions::Contains(string,string,System.StringComparison)` | EQ007 `not-constructible` (no `StringComparison` argument) | EQ007 `not-constructible`: both sides threw `ArgumentException`, which the model's runs do not |
| gitextensions-8522 | `GitExtensions.Plugins.GitStatistics.PieChart.PieSlice::CreateBrushForSide(System.Drawing.Color,double)` | EQ007 `not-constructible` (no `Color` argument) | unchanged: `Color` is a struct, not an enum (P2-257, new) |
| duplicati-3124 | `Duplicati.Library.Compression.FileArchiveDirectory::CreateFile(string,Duplicati.Library.Interface.CompressionHint,System.DateTime)` | EQ007 `not-constructible` (no `CompressionHint` argument) | EQ007 `not-constructible`: the enum `hint` is built; no `System.DateTime` argument can be built for `lastWrite` (P2-257, new) |

Every enum argument here was built as `value`, and in each of the five the model's element was not a member's
constant element: the bodies compare the enum with no constant, so the model leaves it free and replay builds a
value no member defines. The two `reproduced` replays are the runtimes' own difference on such a value: by both
runtimes' sources, .NET Framework's `Environment.GetEnvironmentVariable(string, EnvironmentVariableTarget)` and
`Environment.GetFolderPath(SpecialFolder)` throw `ArgumentException` for an undefined value, and .NET's throw
`ArgumentOutOfRangeException`, so they are EQ006 by ADR 0060 decision 4 on an input the model allows. Neither is in
P2-212's sample and neither is adjudicated against a caller here: the replay shows that the two runtimes differ, not that
Git Extensions passes such a value. `StringExtensions::Contains` is the same choice seen from the other side: both
runtimes reject the undefined `StringComparison`, where the model let the call return, so the case is not the model's.

## Findings

- P2-257 (new): a framework struct the model leaves free (`DateTime`, `Color`) is not built, so `CreateFile` and
  `CreateBrushForSide` stay `not-constructible`.
