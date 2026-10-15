# P2-200 Precision bug: a path row fires on a valid path more than one call away, or validated by a partial validator
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-188

## Goal
P2-188 reads a solution callee one call deep, and its one validator is `Path.GetFullPath`. Eight of
the 16 false EQ006 P2-130 adjudicated to it are still EQ006 for reasons a field does not explain
(P2-201 has those):
- deeper than one call: Duplicati's `LinuxSnapshot::GetCreationTimeUtc` and `GetLastWriteTimeUtc`
  (#36, #44) reach `NormalizePath` through an override and then a nested type's method; Git
  Extensions' `ConfigFileSettings::CreateLocal` (row 52) reads `GitModule.GitCommonDirectory`, which
  caches git's output under a lock, and `AppSettings::get_AvatarImageCachePath()` (row 62) a static
  `Lazy<string>`;
- through a partial validator: Duplicati's `IndexHtmlHandler::Process` (#30) passes to
  `Path.Combine` the result of a helper ending in `Path.Combine`, which on .NET Framework rejects
  `" < > |` and control characters but not `?`, `*` or a stray colon, so its result is a valid path
  for another `Combine` and not for a `FileStream`;
- from the only caller of a private method: Git Extensions' `FileAssociatedIconProvider::CreateTempFile`
  (#38) and Duplicati's `RecoveryTool.Index::WriteIndex` (#43);
- from a `foreach` over `Directory.GetFiles(...)`, not a listed source: Git Extensions'
  `GitModuleTestHelper::Dispose()` (#43).

When done, the largest of these is followed, or `equiv-adr` records why none is.

## Spec references
`docs/tickets/done/P2-188-*.md`, the `validPaths` header of `runtime-changes.json`, ADR 0019.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each of the eight is assigned one of the four routes, and the route followed has its soundness
   argument in Notes; a route past one call level goes through `equiv-adr` first.
2. Rerun the pair or pairs it touches in compare mode quick; Notes give the eight procedures' new
   results and the path-row EQ006 count next to P2-188's (59, 100, 16).

## Tests
Precondition unit tests for the route followed, and one where the path really comes from an argument
and stays flagged.

## Size guard
A call graph over the solution: stop and route it through `equiv-adr`.

## Out of scope
Fields and get-only properties (P2-201). Device-path prefixes (P2-130's undetermined four).

## Notes
- Filed 2026-10-14 by P2-188 (criterion 1).
- Criterion 1, the eight by route, from the legacy checkouts (`.corpus/repos/*@beaf03562fdc`, `@3f4ed21998af`). For each,
  what clears it, since a route that leaves another flagged call in the procedure clears nothing:

  | Pair | # | Procedure | Route | What else it needs |
  |---|---|---|---|---|
  | Duplicati | 36 | `LinuxSnapshot::GetCreationTimeUtc(string)` | deeper than one call | nothing: `ConvertToSnapshotPath` on `this` (the abstract `SnapshotBase` member; `LinuxSnapshot` is sealed and overrides it), then the nested sealed `SnapShot::ConvertToSnapshotPath`, then `SystemIOLinux.NormalizePath`, which ends in `Path.GetFullPath`: three calls |
  | Duplicati | 44 | `LinuxSnapshot::GetLastWriteTimeUtc(string)` | deeper than one call | nothing, as #36 |
  | Git Extensions | row 52 | `ConfigFileSettings::CreateLocal(...)` | deeper than one call | `GitModule.GitCommonDirectory` returns a field it fills under a lock, from git's output kept only when `Directory.Exists` passed on a reassigned local, else from `GetGitDirectory()`: a field (P2-201) and a guard on a local assigned after its declaration, neither of which a deeper read reaches |
  | Git Extensions | row 62 | `AppSettings::get_AvatarImageCachePath()` | deeper than one call | `ApplicationDataPath` is a static `Lazy<string?>` property with a private setter that a test accessor also sets; its factory is a lambda returning `GetGitExtensionsDirectory()` or `Application.UserAppDataPath.Replace(...)`. A field with writers outside its initializer, a lambda and `String.Replace`: not one call level, and past P2-201's size guard |
  | Duplicati | 30 | `IndexHtmlHandler::Process(...)` | partial validator | `GetPath`'s `Path.Combine` result would make the two `Path.Combine(path, ...)` calls unflagged, but `File.OpenRead(File.Exists(html) ? html : htm)` stays flagged on `htm`: what clears it is `Directory.Exists(path)` guarding `path`, which reaches `html` and `htm` only by reading a guard on a local after the locals derived from it were declared |
  | Git Extensions | 38 | `FileAssociatedIconProvider::CreateTempFile(string)` | only caller of a private method | the one caller passes `Path.GetFileName(fullPath)`, `fullPath` a `PathUtil.Combine` of a lambda's captures, under `!_fileSystem.File.Exists(fullPath)`: not a valid path by any rule; only a partial validator (`GetFileName` rejects what `Path.Combine` does) as well clears it |
  | Duplicati | 43 | `RecoveryTool.Index::WriteIndex(...)` | only caller of a private method | the one caller passes `ixfile`, a local whose last write is `Path.GetFullPath(ixfile)` but whose first is an `out` argument: a reaching-definition read of locals as well |
  | Git Extensions | 43 | `GitModuleTestHelper::Dispose()` | `foreach` over `Directory.GetFiles` | `Directory.Delete(TemporaryPath, true)` in the same method reads a get-only property set from a local function (P2-201's #51 shape), so a `foreach` route alone clears nothing |

  Route sizes: deeper than one call 4, only caller 2, partial validator 1, `foreach` 1. Of the four deeper-than-one-call
  results, two are reached by reading deeper and two (rows 52 and 62) are fields that P2-188 counted under it because a
  solution property is the first step; no other route clears any of the eight on its own.
- Decision: the route followed is deeper than one call: the largest (4 of 8) and the only one that clears any of the
  eight alone (#36, #44). It needs two things P2-188 left out: a returned call inside a callee read the same way, and a
  virtual call on a sealed receiver read through the override that runs.
- `equiv-adr` (criterion 1, a route past one call level): bar test, first row. ADR 0019 already decides that a verdict
  assumes its matched callees equivalent, and P2-188 applied it to one callee; a chain of callees is the same decision
  link by link, a case the ADR did not spell out. Recorded as a dated Clarification on ADR 0019, not a new ADR. The
  size guard (a call graph over the solution) is not tripped: the read starts at one call site's argument and follows
  only the calls that argument's value is returned from, never enumerating callers or callees, and stops at four.
- Decision: the bound is four calls from the call site (`ValidPaths.CalleeDepth`). The corpus needs three; a bound is
  needed anyway, since a recursive method would otherwise be read until the stack ran out, and a recursion that reaches
  no valid path within four is flagged, as it was.
- Decision: a virtual call is read only on a receiver whose static type is sealed, through the override that type
  itself declares. An override the sealed type inherits is not looked for: walking its base types has a branch no
  well-formed code reaches (an interface member on a sealed class's receiver), so it could not be covered, and the
  corpus case declares its override. Interface members, `base.` calls and receivers of a type that is not sealed stay
  unread.
- Decision: each callee's body is read with the compilation that holds it (its operation's semantic model), so a
  callee's own callee in a project only the callee's project references is found.
- Soundness. P2-188's argument, applied at each link. A call to a matched solution member is a function both sides
  share; the caller's verdict assumes the callee pair equivalent (ADR 0019) and names it in `unprovenAssumptions` when
  no proof covers it. Under that assumption the callee returns on both runtimes what its legacy body returns. When that
  body returns another solution call, the callee's own verdict assumes that pair equivalent the same way, so the value is
  what the next legacy body returns, and so on to a body whose values are valid by P2-074's rules or a validator's. The
  chain's divergence, if any, is reported where it starts: at the validator's own row in the last body (every
  validator is a path row with every path row's `changedIn`), or as the result of the callee pair that differs, and the
  exit code reflects it. For #36 and #44 that is `NormalizePath`'s `GetFullPath` row, which P2-188 left Unknown
  (`abstraction`). A virtual call on a receiver of sealed type `T` runs `T`'s own override, the only body the runtime
  can dispatch to (a sealed class has no subclasses, and the override `T` declares is the most derived); the verdict
  already rests on that override's pair being equivalent, as every virtual call does, since both sides share the call's
  function. In the runs below the chain is visible as ADR 0019 means it: each `LinuxSnapshot` caller assumes
  `LinuxSnapshot::ConvertToSnapshotPath(string)` (the override, by name), which is Equivalent and assumes
  `LinuxSnapshot.SnapShot::ConvertToSnapshotPath(string)`, which is Equivalent (`bounded`) and names
  `SystemIOLinux::NormalizePath(string)`, Unknown (`abstraction`), in its `unprovenAssumptions`.
- Decision: the before runs are at `main` (`0e18ba4c`), not only P2-188's numbers. P2-189 and P2-190 merged after
  P2-188's runs, so only a run at `main` separates this ticket's change. The `main` build ran from a second worktree at
  a short path: from the session's scratch directory the .NET Framework build host's path passes 260 characters and
  does not start (`duplicati-3124` exited 5 in 1 s; that run was discarded and repeated). `.corpus/` was fetched and
  restored anew for this ticket (P2-188's was deleted with its worktree), Git Extensions' checkouts reused from another
  worktree. Compare mode quick, `--jobs 4`, one run at a time.
- Criterion 2, the eight procedures, P2-188's run, then `main`, then this branch:

  | Pair | # | Procedure | P2-188 | `main` | P2-200 |
  |---|---|---|---|---|---|
  | Duplicati | 36 | `LinuxSnapshot::GetCreationTimeUtc(string)` | EQ006 | EQ006 | Equivalent (`congruence`) |
  | Duplicati | 44 | `LinuxSnapshot::GetLastWriteTimeUtc(string)` | EQ006 | EQ006 | Equivalent (`congruence`) |
  | Git Extensions | row 52 | `ConfigFileSettings::CreateLocal(GitModule,ConfigFileSettings,SettingLevel,bool)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | row 62 | `AppSettings::get_AvatarImageCachePath()` | EQ006 | EQ006 | EQ006 |
  | Duplicati | 30 | `IndexHtmlHandler::Process(...)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | 38 | `FileAssociatedIconProvider::CreateTempFile(string)` | EQ006 | EQ006 | EQ006 |
  | Duplicati | 43 | `RecoveryTool.Index::WriteIndex(...)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | 43 | `GitModuleTestHelper::Dispose()` | EQ006 | EQ006 | EQ006 |

  The public `CreateLocal(GitModule,bool)` is Equivalent (`congruence`) at `main` and here, naming the private overload
  above as unproven. The six left are P2-202 (filed by this ticket), after P2-201.
- Criterion 2, path-row EQ006 (counted as P2-188 did: an EQ006 whose `reviewGroup` names a row with the `invalidPath`
  precondition) and all EQ006:

  | Pair | Path-row EQ006: P2-188, `main`, P2-200 | All EQ006: P2-188, `main`, P2-200 |
  |---|---|---|
  | `gitextensions-8522` | 59, 61, 60 | 157, 142, 141 |
  | `duplicati-3124` | 100, 102, 98 | 130, 121, 117 |
  | `openra-17989` | 16, 16, 16 | 45, 37, 37 |
  | All three | 175, 179, 174 | 332, 300, 295 |

  `main` has two more path-row EQ006 than P2-188's branch on each of the first two pairs and fewer EQ006 in all;
  P2-189 and P2-190 merged in between, and a procedure whose `reviewGroup` named a row P2-190 narrowed now names its
  next runtime-changed call, which can be a path row. That reading was not checked against P2-188's logs (they were
  deleted with its worktree), so the difference is not attributed. Between `main` and this branch every other result
  is the same. The five that changed, all EQ006 to Equivalent (`congruence`) and all correct by reading the source:
  #36 and #44; Duplicati's `LinuxSnapshot::GetAttributes(string)` and `LinuxSnapshot::OpenRead(string)`, the same
  `File` call on `ConvertToSnapshotPath(localPath)`; and Git Extensions' `GitCommandsTests.Config.ConfigFileTest::TestSave()`,
  `File.ReadAllBytes(GetConfigFileName())`, a `Path.Combine` of `GetTempFolder()`, which returns `Path.GetTempPath()`:
  two calls deep. No result became Divergent or Unknown. Run times at `main` and this branch: 832 s and 447 s,
  664 s and 509 s, 403 s and 331 s; the `main` Git Extensions run overlapped a test run and two builds, so the
  difference is not attributed.
- Decision: no `Release:` footer. A precision fix that changes no surface is a patch (`equiv-release`).
