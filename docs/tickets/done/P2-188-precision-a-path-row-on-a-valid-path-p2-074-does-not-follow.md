# P2-188 Precision bug: a path row fires on a valid path that reaches the call through a solution member, a field or a caller
Status: done (PR #497)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-074 (done) stops a path-validation row from firing on a path from a listed source, transform or
guard inside the method, and "a path from a caller or a field is not followed". P2-130's
adjudication (`docs/runs/2026-10-14-migrations-verdict.md`) found 13 false EQ006 in its samples (5 on
`gitextensions-8522`, plus 3 more of P2-047's rows 52, 61 and 62, 7 on `duplicati-3124`, 1 on
`openra-17989`), all a valid path that reaches the call by a route P2-074 does not follow:
- through a solution method or property that only returns a valid path (`Translator.GetTranslationDir()`,
  `GitModule.GitCommonDirectory`, `AppSettings.ApplicationDataPath`, `IndexHtmlHandler.GetPath(Uri)`);
- through a solution helper whose last step is a validating path member on the legacy runtime
  (`SystemIOLinux.NormalizePath` returning `Path.GetFullPath`; `GitModule.WorkingDir`, set through
  `GetFullPath` in the constructor), so the legacy call never sees an invalid path;
- through a field or get-only property set from a source (the test directory, `Path.GetTempPath()`),
  or a property whose writers store only paths a `FileStream` or `File.Move` accepted;
- through the only caller of a private method;
- from `Directory.GetFiles(...)`, which is not a listed source, in a `foreach` variable.

The appendix of the verdict lists every procedure. When done, the largest of these routes is followed.

## Spec references
`docs/tickets/done/P2-074-*.md` and `P2-073-*.md`, the row preconditions in
`src/Equiv.Core/RuntimeChanges/`, ADR 0040.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count the routes over the 13 results and the pairs' other path-row EQ006, and record them.
2. Follow the largest route (at least: a validating path member's result is a valid-path source, also
   through a solution-local method that returns it), with the soundness argument in Notes.
3. Rerun the three migration pairs in compare mode quick; Notes give each listed procedure's new
   result and the path-row EQ006 count next to today's.

## Tests
Precondition unit tests per route followed; a pair where the path really comes from an argument and
stays EQ006.

## Size guard
An interprocedural analysis beyond one call level: stop and route it through `equiv-adr`.

## Out of scope
Device-path prefixes (`\\?\`): the verdict leaves Duplicati's 4 such results undetermined.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
- Criterion 1, the 16 adjudicated results (the 13, and P2-047's rows 52, 61 and 62), by route, from P2-130's
  hand traces (`.corpus/audit/P2-130/`) and the checkouts:

  | Route | Results | Of them, one call level and a validator reach |
  |---|---|---|
  | A solution method or property returns the path | 9: Git Extensions #29, rows 52 and 62; Duplicati #30, #35, #36, #41, #44, #52 | 4: #29, #35, #41, #52 |
  | A field or get-only property holds it | 4: Git Extensions row 61, #51, #55; OpenRA #8 | 0 |
  | The only caller of a private method passes it | 2: Git Extensions #38; Duplicati #43 | 0 |
  | A `foreach` variable over `Directory.GetFiles` | 1: Git Extensions #43 | 0 |

  Of the solution-member route, the five Duplicati results whose helper ends in `Path.GetFullPath`
  (`SystemIOLinux::NormalizePath`) are the validator case the criterion names. #36 and #44 reach it three calls deep,
  through `LinuxSnapshot::ConvertToSnapshotPath`, an override, then the nested snapshot's own `ConvertToSnapshotPath`;
  #30's helper ends in `Path.Combine`, which validates fewer characters than the `invalidPath` precondition names
  (`?`, `*` and a stray colon pass it); row 52 is a property that caches git's output under a lock and row 62 a static
  `Lazy<string>`.
- Criterion 1, every path-row EQ006 of the three pairs (181 in P2-130's runs at `e98fd1b9`; 181 at `main`, `fc7d46ca`:
  61 Git Extensions, 104 Duplicati, 16 OpenRA), by the syntactic kind of the flagged call's argument. This is a
  script's reading of the legacy source at each result's location, not a hand trace, so it counts shapes, not false
  positives: 71 a parameter of the method (a caller's path), 53 a field or property (33 of the method's own type, 20
  of another object), 32 a call of a solution or other member, 12 another expression (concatenation, interpolation),
  6 a BCL path or environment member, 4 a local, 3 a `foreach` variable. A path row is one whose `precondition` is
  `invalidPath`; a result is counted under the row its `reviewGroup` names.
- Decision: the route followed is the solution member, through a validator. It is the largest among the adjudicated
  false positives (9 of 16) and the one criterion 2 names. Over all path-row EQ006 the field-or-property shape (53)
  is larger than the call shape (32), but none of its adjudicated four is reached by reading a field's writers
  alone (a local function, a solution extension method, two writers in other methods, a `Lazy`), and the parameter
  shape (71) is mostly public methods whose callers are outside the solution. The field route is P2-201; what one
  call level leaves is P2-200.
- Decision: `validators` is its own list in `runtime-changes.json`, not an entry in `sources`. Its soundness rests on
  a condition a source does not have (below), which `EveryValidatorIsAPathRowWithEveryPathRowsChange` checks for
  every entry. `Path.GetFullPath(string)` moved there from `transforms`; it is the only member that returns a path
  and, on .NET Framework, rejects every character `invalidPath` names.
- Decision: `prefixes` is a fifth list, for `String.Substring(int,int)` with the constant 0 as `startIndex`. Duplicati's
  `NormalizePath` drops a trailing separator with it, so without it the validator reaches none of the corpus results.
  A start other than 0 can move a colon off the drive letter (`"C:\x".Substring(1)`), so the rule reads the constant;
  `Substring(int)` is not listed.
- Decision: a conditional expression is a valid path when both branches are (`NormalizePath`'s return).
- Decision: a solution callee is read only when it is an ordinary method or a property getter that is not virtual,
  abstract or an override, so the body read is the one that runs; a local function, a delegate's `Invoke` and an
  interface member are not. Its body is read with the compilation that holds its syntax, the caller's or a project
  the caller references as source, and calls in it to other solution members are not followed (the size guard).
  It must return at least once; returns in its lambdas and local functions are not its own.
- Soundness. Verdicts are modular (ADR 0019): a call to a matched solution member is a function both sides share,
  and the caller's verdict assumes the callee pair equivalent and names it in `unprovenAssumptions` when no proof
  covers it. Under that assumption the callee returns what its legacy body returns, and every value that body
  returns is a valid path by P2-074's rules or by a validator. A validator is sound because .NET Framework's
  `GetFullPath` throws (`ArgumentException`, or `NotSupportedException` for a stray colon) for every path
  `invalidPath` names, so the value it returns has none of those characters; the one exception is a device path
  (`\\?\`, `\\.\`), which it returns unchecked and which no later .NET Framework validation checks either, so no row's
  change is reached by it (the reading P2-130's verdict rests its device-path decision on, not a run). On another
  runtime `GetFullPath` returns an invalid path unchanged, and then the validator's own row fires on its argument,
  in the method that calls it: every validator is a path row, and every path row has the same `changedIn`
  (`netcoreapp1.0`), so any pair that crosses the row being decided crosses the validator's too. The divergence is
  therefore reported where it starts, at the validator's call. In the corpus that is `NormalizePath`, which stays
  Unknown (`abstraction`, P2-195's case) and is in the `unprovenAssumptions` of each caller this ticket makes
  congruent.
- Known gaps, all on the side of not flagging, as P2-074's: `Substring(0, n)` with `n` 0 gives an empty string, which
  `GetDirectoryName` and `GetFullPath` reject on .NET Framework only; the start `\\?` of a bare `\\?\` is no longer a
  device path and has a `?` (a device-path case, out of scope here); a project that lists `GetFullPath` in
  `suppressRuntimeChanges` silences its row, and the path rows after it stay quiet too; a path of 248 to 259
  characters passes `GetFullPath` where .NET Framework blocks long paths and fails a directory call, the gap P2-074's
  sources already have.
- Decision: no `Release:` footer. A precision fix that changes no surface is a patch (`equiv-release`).
- Decision: the before runs are at `main` (`fc7d46ca`), not only P2-130's `e98fd1b9`. P2-181 and P2-168 merged after
  P2-130's runs, so only a run at `main` separates this ticket's change. P2-130's counts are listed too. Both sets of
  runs reuse P2-130's `.corpus` (its checkouts and restores, linked into this worktree), compare mode quick,
  `--jobs 4`, one pair at a time.
- Criterion 3, each listed procedure, P2-130's run, then `main`, then this branch:

  | Pair | # | Procedure | P2-130 | `main` | P2-188 |
  |---|---|---|---|---|---|
  | Git Extensions | 29 | `TranslationApp.Program::Main()` | EQ006 | EQ006 | Unknown (`abstraction`, `String.Concat`) |
  | Git Extensions | 38 | `GitCommands.FileAssociatedIconProvider::CreateTempFile(string)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | 43 | `CommonTestUtils.GitModuleTestHelper::Dispose()` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | 51 | `GitUITests...FormFileHistoryControllerTests::TryGetExactPathName_should_check_if_path_matches_case(...)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | 55 | `GitUI.CommandsDialogs.FormResolveConflicts::Mergetool_Click(...)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | row 52 | `GitCommands.Settings.ConfigFileSettings::CreateLocal(...)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | row 61 | `GitCommandsTests.Git.Tag.GitTagControllerTest::Setup()` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | row 62 | `GitCommands.AppSettings::get_AvatarImageCachePath()` | EQ006 | EQ006 | EQ006 |
  | Duplicati | 30 | `Duplicati.Server.WebServer.IndexHtmlHandler::Process(...)` | EQ006 | EQ006 | EQ006 |
  | Duplicati | 35 | `Duplicati.Library.Common.IO.SystemIOLinux::DirectoryDelete(string,bool)` | EQ006 | EQ006 | Equivalent (`congruence`) |
  | Duplicati | 36 | `Duplicati.Library.Snapshots.LinuxSnapshot::GetCreationTimeUtc(string)` | EQ006 | EQ006 | EQ006 |
  | Duplicati | 41 | `Duplicati.Library.Common.IO.SystemIOLinux::DirectoryCreate(string)` | EQ006 | EQ006 | Equivalent (`congruence`) |
  | Duplicati | 43 | `Duplicati.CommandLine.RecoveryTool.Index::WriteIndex(...)` | EQ006 | EQ006 | EQ006 |
  | Duplicati | 44 | `Duplicati.Library.Snapshots.LinuxSnapshot::GetLastWriteTimeUtc(string)` | EQ006 | EQ006 | EQ006 |
  | Duplicati | 52 | `Duplicati.Library.Common.IO.SystemIOLinux::GetFileAttributes(string)` | EQ006 | EQ006 | Equivalent (`congruence`) |
  | OpenRA | 8 | `OpenRA.FileFormats.ReplayMetadata::RenameFile(string)` | EQ006 | EQ006 | EQ006 |

  4 of the 16 leave EQ006, the four criterion 1 counted as one call level and a validator away. The twelve left
  are P2-201 (row 61, #51, #55, OpenRA #8) and P2-200 (#30, #36, #44, rows 52 and 62, #38, Duplicati #43, Git
  Extensions #43).
- Criterion 3, path-row EQ006 and all EQ006:

  | Pair | Path-row EQ006: P2-130, `main`, P2-188 | All EQ006: P2-130, `main`, P2-188 |
  |---|---|---|
  | `gitextensions-8522` | 61, 61, 59 | 166, 159, 157 |
  | `duplicati-3124` | 104, 104, 100 | 142, 134, 130 |
  | `openra-17989` | 16, 16, 16 | 48, 45, 45 |
  | All three | 181, 181, 175 | 356, 338, 332 |

  Every other result is the same between `main` and this branch. The six that changed: the four above, and two
  not in the samples, both correct by reading the source: `GitCommandsTests.Config.ConfigFileTest::GetConfigFileName()`
  (`Path.Combine` of a private method returning `Path.GetTempPath()`) and Duplicati's
  `SystemIOLinux::PathGetDirectoryName(string)` (`Path.GetDirectoryName(NormalizePath(path))`), both now congruent.
  No result became Divergent and none became Equivalent but these five. Run times at `main` and this branch:
  603 s and 421 s, 580 s and 542 s, 299 s and 290 s; one run each, so the difference is not attributed.
- The sample's `Full` (the `NormalizePath` shape) is Unknown (`abstraction`), not EQ006: the solver's divergence
  depends on `String.Length`, `get_Chars` and `Substring` as well as on the `GetFullPath` row. That is P2-195's case
  (an `abstraction` Unknown that names a shared function, not the runtime-changed call), as is Duplicati's
  `NormalizePath`, and `TranslationApp.Program::Main()`'s new Unknown names `String.Concat`.
