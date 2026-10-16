# P2-208 Precision bug: a path row fires on a valid path a field holds through a local function, a trailing separator or a private setter
Status: done (PR #529)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-201

## Goal
P2-201 reads a `readonly` field or get-only auto-property of a class through its initializer and the writes in its
type's constructors and `init` accessors. Three of the four false EQ006 P2-130 adjudicated to it are still EQ006,
because the value written is one no rule shows valid (P2-201's Notes have each trace):
- Git Extensions #51, `FormFileHistoryControllerTests::TryGetExactPathName_should_check_if_path_matches_case(...)`:
  `GitModuleTestHelper.TemporaryPath` is written from a local function returning `Path.Combine(tempPath,
  Path.GetRandomFileName())`, `tempPath` being `Path.GetTempPath()` or `"/private" + tempPath`;
- Git Extensions #55, `FormResolveConflicts::Mergetool_Click(...)`: `GitModule.WorkingDir` is written as
  `(workingDir ?? "").NormalizePath().EnsureTrailingPathSeparator()`, where `NormalizePath` returns `string.Empty` for a
  blank path and `Path.GetFullPath(...)` otherwise, and `EnsureTrailingPathSeparator` appends a separator with `+=`;
- OpenRA #8, `ReplayMetadata::RenameFile(string)`: a property with a private setter whose writers, in other methods,
  store null or a path a `FileStream` or `File.Move` accepted.

When done, the largest of these is followed, or `equiv-adr` records why none is.

## Spec references
`docs/tickets/done/P2-201-*.md`, `P2-200-*.md` and `P2-188-*.md`, the `validPaths` header of `runtime-changes.json`,
ADR 0019 and its P2-200 clarification.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each of the three is assigned the route that clears it, and the route followed has its soundness argument in Notes.
2. Rerun the pair or pairs it touches in compare mode quick; Notes give the three procedures' new results and the
   path-row EQ006 count next to P2-201's (56, 98, 16).

## Tests
Precondition unit tests for the route followed, and one where the path really comes from an argument and stays flagged.

## Size guard
A call graph over the solution, or reading a member's writers outside its own type: stop and route it through `equiv-adr`.

## Out of scope
Device-path prefixes (P2-130's undetermined four). P2-207's six.

## Notes
- Filed 2026-10-15 by P2-201 (criterion 2).
- Criterion 1, the three by route, from the legacy checkouts (`.corpus/repos/*@3f4ed21998af`, `@fef7a018f2b2`) and the
  counterexamples of the 2026-10-15 runs. For each, what clears it, since a route that leaves another flagged call in
  the procedure clears nothing:

  | Pair | # | Procedure | Route that clears it | Owner |
  |---|---|---|---|---|
  | Git Extensions | 51 | `FormFileHistoryControllerTests::TryGetExactPathName_should_check_if_path_matches_case(string,bool,bool)` | `TemporaryPath`: a local function read as a callee, `Path.GetRandomFileName` as a source, and `"/private" + tempPath` under `tempPath.StartsWith("/var")` (this ticket). Not enough alone: the method's second `Path.Combine(repo.TemporaryPath, relativePath)` takes `relativePath`, a parameter of the public test method, which the counterexample makes symbolic. Only NUnit's `[TestCase]` constants reach it, and a public method's callers are read nowhere (a solution-wide caller search, and a runner that calls by reflection) | `TemporaryPath`: this ticket; `relativePath`: recorded, not followed |
  | Git Extensions | 55 | `FormResolveConflicts::Mergetool_Click(object,EventArgs)` | `GitModule.WorkingDir`, written once in the constructor, which P2-201 reads, as `(workingDir ?? "").NormalizePath().EnsureTrailingPathSeparator()`: a public extension that returns its own parameter after `+=` of a `static readonly char` field set to `Path.DirectorySeparatorChar`, on `NormalizePath`'s `string.Empty` or `Path.GetFullPath(...)`. Three rules: a callee's returned parameter read as the call's argument, a compound append of a separator field, and an empty string's reach read per row | P2-232 |
  | OpenRA | 8 | `ReplayMetadata::RenameFile(string)` | none: `Path.GetDirectoryName(FilePath)` fires first, and after it `Path.Combine(dir, newFilenameWithoutExtension)` takes the public method's parameter. Its one caller, `ReplayBrowserLogic::RenameReplay`, passes a name a dialog's validation lambda checked with `IndexOfAny(Path.GetInvalidFileNameChars())` before the dialog's accept callback runs: a library's contract, not code a rule reads. `FilePath`'s private setter is written in a private constructor from `Read(string)`'s parameter and in `RenameFile` itself | recorded, not followed |

  The 2026-10-15 verdict gave P2-208 two more: Git Extensions #28, `FileSettingsCacheTests::SaveImpl_should_create_folder_if_absent()`,
  whose rows all read `Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())` (this ticket's source), and OpenRA
  #13, `GameSaveBrowserLogic::Load()`, which reads the private mutable field `selectedSave`, written in other methods
  from a private method's parameter and from a dialog-validated name: #8's case, recorded, not followed.
- Decision: the route followed is `TemporaryPath`'s. Of the routes the three need, it is the only one that clears a
  result on its own (#28, by its source), it is the step P2-229's #43 (`GitModuleTestHelper::Dispose()`,
  `Directory.Delete(TemporaryPath, true)`) waits on, and it stays inside the size guard. #55's route clears #55 alone
  and is the step P2-228's row 52 waits on, but it is three rules, one of which (an empty string per row) is a
  catalogue-wide reading of the path rows: filed as P2-232. #51's `relativePath` and #8's name need a public method's
  callers, past the size guard of every valid-path ticket since P2-188; `equiv-adr` bar test, third row: recorded here
  as not followed, as P2-207 recorded row 62, no ADR.
- Decision: a local function is read as a callee only when it reads no local variable or parameter of a method around
  it (each local or parameter its body reads is declared in it, or in a lambda in it). It is not a matched procedure,
  so ADR 0019's assumption is not what makes its body the one that runs: a call of a local function is a fragment of
  the method that calls it, fingerprinted by the function's own bound code (ADR 0024 as clarified by P2-171), so the
  two sides' functions are compared where they are called, and a local function cannot be overridden. A function that
  reads nothing around it returns, on each side, the values its own body returns wherever it is called from. One that
  captures a variable is not read: P2-074's guard rule reads a guard before the function's declaration, and the
  function can be called before that guard (P2-233, below).
- Decision: `Path.GetRandomFileName` is a source. On .NET Framework and .NET it returns eight characters, a dot and
  three characters, each from `a` to `z` or `0` to `5` (a base-32 encoding of random bytes): no character
  `invalidPath` names, no colon, never blank.
- Decision: a valid path followed by a string constant with no control character, none of `" < > | ? *` and no colon is
  one (`RuntimeChangeTable.JoinsValidPath`); before a valid path, such a constant is one only where the path has no
  drive. A valid path's one colon follows its drive letter; a constant after it leaves that colon where it is, a
  constant before it moves the colon off its place (`"/private" + "C:\Temp\"`), which .NET Framework rejects. Only a
  built-in `+` is read (no operator method); with a string constant operand it joins strings.
- Decision: a path has no drive where `String.StartsWith` returned true for it on a constant that begins with `/` or
  `\` and then an ASCII letter or digit, read where a guard counts (P2-074's places) with nothing between the guard and
  the read that runs code or writes the path (P2-217's `Quiet`, which now also refuses an assignment to the variable
  itself). The path is a local variable or by-value parameter; a field is not read. Its second character is then not a
  colon under an ordinal or a culture comparison alike: the constant's two characters must each be matched, in order,
  by characters of the path that are not ignorable, a colon is not ignorable and compares equal to neither a separator
  nor a letter or digit, and no single character expands to a separator and a letter. A colon after the second
  character is not a valid path's, so the path has none. `StartsWith(char)` and a constant that is not a string are
  not read.
- Decision: a local's writes are read with the local itself, and every local whose writes are being read, taken to
  hold valid paths. Each assignment runs when the locals already hold what earlier ones gave them, so if every
  assignment gives a valid path from valid paths, the local only ever holds valid paths (an induction over the run's
  assignments). P2-207's limit, `a` and `b` copied into each other in a loop, is now valid; a loop that also gives one
  of them a parameter is the limit.
- Criterion 1, soundness. `TemporaryPath` is a get-only auto-property, so P2-201 reads its constructor's one write, a
  call of the local function `GetTemporaryPath`, which reads nothing around it. Its body returns
  `Path.Combine(tempPath, Path.GetRandomFileName())`: a transform of two valid paths, `GetRandomFileName` a source and
  `tempPath` a local whose writes are `Path.GetTempPath()`, a source, and `"/private" + tempPath` under
  `tempPath.StartsWith("/var")`, where `tempPath` is valid by the induction above and has no drive by the guard. Each
  value is valid on .NET Framework, so no row's change is reached by it, whatever runtime the other side runs; the
  modern side's function is read the same way in its own compilation, and the two calls are one fragment only when the
  functions' bound code matches. Known gaps, all on the side of not flagging, as P2-201's and P2-207's are: a joined
  constant makes a path longer, and a path of 248 characters or more passes these rules where .NET Framework rejects
  it, P2-074's long-path gap; a device path (`\\?\`, `\\.\`) has a colon after its second character and is out of
  scope (P2-130's undetermined four); reflection, `__makeref` and unsafe code write a local unseen, as P2-207 records.
- Found in passing: P2-074's guard rule (`IsGuarded`) walks up through a local function's declaration to a guard in the
  enclosing block, though the function can be called before the guard; a unit probe confirmed it on `main`
  (`c74d3068`). Filed as P2-233, not fixed here; this ticket's own rules stop at a local function.
- Decision: no `Release:` footer. A precision fix that changes no surface is a patch (`equiv-release`).
- Decision: the before runs are at `main` (`c74d3068`), from a detached worktree at `D:\eq208` (P2-200's path-length
  note); this branch ran at `adba9828`. Both used the `.corpus` of the `precision-audit-large-pairs` worktree, its
  checkouts and restores linked in by junction, with `corpus.ps1 -Env` set (a first pass without it skipped every legacy
  .NET Framework project of Git Extensions and Duplicati, exit 4, and was discarded; its OpenRA runs, which need no
  reference assemblies, are the ones below). Compare mode quick, `--jobs 4`, one run at a time. Every run exited 1 (a
  Divergent result), no project skipped.
- Criterion 2, the three procedures, P2-201's run, then `main`, then this branch:

  | Pair | # | Procedure | P2-201 | `main` | P2-208 |
  |---|---|---|---|---|---|
  | Git Extensions | 51 | `GitUITests...FormFileHistoryControllerTests::TryGetExactPathName_should_check_if_path_matches_case(string,bool,bool)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | 55 | `GitUI.CommandsDialogs.FormResolveConflicts::Mergetool_Click(object,System.EventArgs)` | EQ006 | EQ006 | EQ006 |
  | OpenRA | 8 | `OpenRA.FileFormats.ReplayMetadata::RenameFile(string)` | EQ006 | EQ006 | EQ006 |

  All three stay EQ006, as criterion 1's table says they must: #51 on `Path.Combine(repo.TemporaryPath, relativePath)`,
  `relativePath` the test method's parameter (`TemporaryPath` itself is now a valid path); #55 on
  `SetCurrentDirectory(Module.WorkingDir)` (P2-232); #8 on `GetDirectoryName(FilePath)` and then the public parameter.
- Criterion 2, path-row EQ006 (counted as P2-188 did: an EQ006 whose `reviewGroup` names a row with the `invalidPath`
  precondition) and all EQ006:

  | Pair | Path-row EQ006: P2-201, P2-207, `main`, P2-208 | All EQ006: P2-201, P2-207, `main`, P2-208 |
  |---|---|---|
  | `gitextensions-8522` | 56, 54, 54, 52 | 137, 130, 130, 128 |
  | `duplicati-3124` | 98, 94, 94, 94 | 117, 113, 114, 114 |
  | `openra-17989` | 16, 16, 16, 16 | 37, 34, 34, 34 |
  | All three | 170, 164, 164, 162 | 291, 277, 278, 276 |

  `main` has one EQ006 more on Duplicati than P2-207's branch, with the same path-row count; P2-221 and P2-193 merged in
  between, and that difference is not attributed. Between `main` and this branch every other result of the three pairs
  is the same. The two that changed, both on Git Extensions, both EQ006 to Equivalent (`congruence`) and both correct by
  reading the source: the 2026-10-15 verdict's #28, `GitCommandsTests.Settings.FileSettingsCacheTests::SaveImpl_should_create_folder_if_absent()`,
  and `GitCommandsTests.Helpers.PathUtilTest::DeleteWithExtremePrejudice_should_return_true_if_delete_successful()`,
  each a local `Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())` passed to path calls. No result became
  Divergent or Unknown. `GitModuleTestHelper::Dispose()` stays EQ006 on `File.GetAttributes(file)` in its `foreach`
  (P2-229). Run times at `main` and this branch: Git Extensions 345 s and 338 s, Duplicati 451 s and 464 s, OpenRA
  217 s and 210 s; one run each, so the difference is not attributed.
