# P2-232 Precision bug: a path row fires on a valid path a trailing separator, an empty string or a callee's own parameter carries
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-208

## Goal
P2-208 joins a string constant to a valid path. Git Extensions #55, `FormResolveConflicts::Mergetool_Click(object,EventArgs)`,
is still EQ006 on `Directory.SetCurrentDirectory(Module.WorkingDir)`. `GitModule.WorkingDir` is a get-only
auto-property its constructor sets once, which P2-201 reads, to
`(workingDir ?? "").NormalizePath().EnsureTrailingPathSeparator()` (P2-208's Notes have the trace). Three things no
rule reads stand between it and a valid path:
- `EnsureTrailingPathSeparator(this string? dirPath)` is a public extension that returns its own parameter, after
  `dirPath += NativeDirectorySeparatorChar` under a check that it is not empty and does not end in a separator: a
  callee's returned parameter is read only as its caller's argument would be, and P2-188 to P2-207 read a callee's
  body alone;
- the value appended is a `static readonly char` field of the solution set to `Path.DirectorySeparatorChar`, a
  framework field with no C# constant, through a compound assignment, which `LocalValues.Write` gives no value;
- `NormalizePath` returns `string.Empty` for a blank path (and from a `catch`), else `Path.GetFullPath(...)`, so
  `WorkingDir` can be empty. A blank path meets `invalidPath`, though `Directory.SetCurrentDirectory("")` throws
  `ArgumentException` on .NET Framework and on .NET alike: whether an empty string reaches a row's change is a
  property of the row (`Path.GetDirectoryName("")` throws on .NET Framework and returns null on .NET).

Clearing #55 also clears the step P2-228's row 52 (`GetGitDirectory()` reads `WorkingDir`) and P2-218's
`WorkingDir` case wait on, and OpenRA #15's `Platform.SupportDir` (P2-228) appends a separator too.

When done, the largest of these is followed, or `equiv-adr` records why none is.

## Spec references
`docs/tickets/done/P2-208-*.md`, `P2-201-*.md` and `P2-188-*.md`, the `validPaths` header and the `invalidPath`
precondition in `runtime-changes.json`, ADR 0019.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each of the three is assigned a rule or recorded as not followed, and each rule followed has its soundness
   argument in Notes (for the empty string: the rows it does not reach, each with the documented behaviour on both
   runtimes).
2. Rerun `gitextensions-8522` in compare mode quick; Notes give #55's new result and the path-row EQ006 count next
   to P2-208's.

## Tests
Precondition unit tests for each rule followed, and one where the path really comes from an argument and stays
flagged.

## Size guard
A call graph over the solution, or reading a member's writers outside its own type: stop and route it through
`equiv-adr`.

## Out of scope
Device-path prefixes (P2-130's undetermined four). P2-228's fields filled on first use.

## Notes
- Filed 2026-10-16 by P2-208 (criterion 1).
- Criterion 1, the three by rule. Git Extensions' legacy (`3f4ed21998af`) and modern (`5190ba5c1a5f`) checkouts have the
  same `GitModule` constructor, `PathUtil.NormalizePath`, `PathUtil.EnsureTrailingPathSeparator` and
  `NativeDirectorySeparatorChar`; `FormResolveConflicts.Module` is typed `GitModule` (sealed) on both, so P2-201 reads
  `WorkingDir`'s one write.

  | # | What stands between `WorkingDir` and a valid path | Rule |
  |---|---|---|
  | 1 | `EnsureTrailingPathSeparator(this string? dirPath)` returns its own parameter | a callee's by-value parameter is read as the call's argument (followed) |
  | 2 | `dirPath += NativeDirectorySeparatorChar`, a `static readonly char` set to `Path.DirectorySeparatorChar` | a char path part and a compound `+=` (followed) |
  | 3 | `NormalizePath` returns `string.Empty` for a blank path and from its `catch` | a valid path or the empty string, read per row (followed) |

- Decision: a callee's by-value parameter is a valid path where its body reads it when the call passes one (read where
  the call is, by these rules) and every write of it in the body gives one, the parameters so passed taken to hold valid
  paths (`ValidPaths.Passed`). Soundness: a by-value parameter starts as the argument and only the body can write it, its
  lambdas and local functions included (a write through a `ref` to it is a write `LocalValues.Writes` gives no value, so
  the parameter is dropped). Each write runs when the parameters already hold what the call and earlier writes gave them,
  so if every write keeps them valid they only ever hold valid paths, P2-208's induction for locals. A parameter whose
  write does not is dropped and the rest are read again, a greatest fixed point. The body is the one that runs by P2-188's
  and P2-200's rules, a callee pair assumed equivalent as ADR 0019 and its P2-200 clarification say; a `ref`, `out` or
  `in` parameter is not read (the caller's variable can change under it).
- Decision: a char joins a valid path as a string constant does (P2-208's `JoinsValidPath`), whether by `+` or `+=`: a
  char constant, `Path.DirectorySeparatorChar` or `Path.AltDirectorySeparatorChar` (the table's new `validPaths.separators`;
  each is `\` or `/` on every runtime and operating system), or a `readonly` field of the solution whose initializer and
  every write in its type's constructors is one, read as P2-201 reads a stored path (each a step toward the bound of four).
  `Path.VolumeSeparatorChar` (`:`) is not listed. A compound `+=` is now a write `LocalValues.Writes` gives a value for,
  the assignment itself, read as the join of its target's value before it and its operand. A variable read as a path holds
  a string, and `+=` on a string is the built-in join, so no operator method can stand behind it: a target that is not a
  string is never a valid path to start from. A `??=` stays a write with no value.
- Decision: the empty string is read per row, through a second reading of the same rules. An operand is a valid path or
  the empty string (`CallArgument.ValidPathOrEmpty`) when the rules show it to be one with `""` and `String.Empty` taken
  as such; a constant joined to such a value must also be empty or not blank, since joined to the empty string it is the
  whole path; a transform's or a prefix's operands are still read as valid paths, with nothing the second reading assumed
  of a variable. A row marked `emptyPathUnchanged` does not fire on such an operand. Marked, each with its source on both
  runtimes:
  - `Directory.SetCurrentDirectory(string)`: .NET Framework 4.8 throws `ArgumentException` (`Argument_PathEmpty`) when
    `path.Length == 0`, before any other check (referencesource `mscorlib/system/io/directory.cs`); .NET 10 calls
    `ArgumentException.ThrowIfNullOrEmpty(path)` first (dotnet/runtime `System.Private.CoreLib/src/System/IO/Directory.cs`).
    Both throw `ArgumentException`.
  - `Path.Combine(string,string)`, `(string,string,string)` and `(string,string,string,string)`: .NET Framework checks each
    part for invalid characters, which `""` has none of, and its `CombineNoChecks` skips an empty part; .NET 10's
    `CombineInternal` skips an empty part the same way (dotnet/runtime `System/IO/Path.cs`). Both return the other parts
    joined.

  Not marked, so the empty string still reaches them: every other `invalidPath` row, among them `Path.GetDirectoryName`
  (.NET Framework throws `ArgumentException` on `""`, .NET returns null) and every member whose empty-string behaviour this
  ticket did not read on both runtimes. Gaps, all on the side of not flagging as P2-208's are: the long-path gap, device
  paths, and reflection or unsafe code writing a parameter unseen.
- Found while rerunning, and fixed here because #55 waits on it: P2-201's read of a stored path in another project never
  matched that project's writes in Git Extensions. `GitUI` sees `GitCommands` through retargeting symbols (it binds some
  shared reference to another version; both have the same `mscorlib`), and a retargeting `GitModule.WorkingDir` equals
  none of the six references to it in `GitModule`'s own constructor, so `Writes` found none (a probe on the legacy
  solution, not committed, showed 0 of 6 equal). `Writes` now resolves the member to the symbol its own project
  declares before collecting its writes (`ValidPaths.Declared`). Soundness is unchanged: the declaration is the member's
  own, and the writes read are still every write the language allows. The unit test forces retargeting with two versions
  of one dependency.
- Decision: no `Release:` footer. A precision fix that changes no surface is a patch (`equiv-release`); the table's new
  `emptyPathUnchanged` and `separators` are read only by this repo.
- Decision: the before run is at `main` (`8645e40e`, which holds P2-208) from a detached worktree at `D:\eq232`; this
  branch ran at `a10220de`. Both used the `.corpus` of the `precision-audit-large-pairs` worktree by junction, with
  `corpus.ps1 -Env` set. Compare mode quick, `--jobs 4`, one run at a time; each exited 1 (a Divergent result) with no
  project skipped. A first branch run at `08567e45`, before the retargeting fix, left #55 EQ006 and is superseded.
- Criterion 2: #55, `GitUI.CommandsDialogs.FormResolveConflicts::Mergetool_Click(object,System.EventArgs)`, is EQ006 at
  P2-208 and at `main`, and **Equivalent (EQ001)** on this branch. Path-row EQ006 (counted as P2-188 did) and all EQ006
  on `gitextensions-8522`:

  | Run | Path-row EQ006 | All EQ006 | Run time |
  |---|---|---|---|
  | P2-208 | 52 | 128 | 338 s |
  | `main` (`8645e40e`) | 52 | 128 | 389 s |
  | P2-232 (`a10220de`) | 48 | 124 | 395 s |

  The seven results that changed between `main` and this branch, each correct by reading the source, each reading
  `GitModule.WorkingDir` or `GitModule`'s own `WorkingDir`:
  - EQ006 to Equivalent: #55 and `FormResolveConflicts::customMergetool_Click(object,EventArgs)`, both
    `Directory.SetCurrentDirectory(Module.WorkingDir)`;
  - EQ006 to Unknown: `GitModule::CheckoutConflictedFiles(ConflictData)` (its `SetCurrentDirectory(WorkingDir)` no longer
    fires; `opaque:LocalFunction` remains) and `FormEditorTests::HasChanges_updated_correctly()` (`Path.Combine` on
    `WorkingDir` no longer fires; `abstraction` remains);
  - Unknown to Equivalent: `GitModule::HandleConflictSelectSide(string,string)`,
    `GitDirectoryResolverTests::Resolve_submodule_real_filesystem()` and
    `RevisionGridControlTests::ToggleBetweenArtificialAndHeadCommits_no_index_change(bool)`, each with a path call on
    `WorkingDir` that no longer fires.

  No result became Divergent or Unknown from Equivalent. P2-218's #36 (`GitModule::InTheMiddleOfMerge()`) stays EQ006:
  besides its interface call, `GitDirectoryResolver.Resolve` returns a path read from a `.git` file (P2-218's Notes). P2-228's
  row 52 (`ConfigFileSettings::CreateLocal`) has no result in either run of this pair. The P2-208 run's time was on another
  day's load; the two runs here differ by 6 s, one run each, not attributed.
