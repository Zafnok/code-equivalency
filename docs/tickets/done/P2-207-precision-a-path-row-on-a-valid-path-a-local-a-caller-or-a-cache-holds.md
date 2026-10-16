# P2-207 Precision bug: a path row fires on a valid path that a reassigned local, a private method's caller or a cached field holds
Status: done (PR #524)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-200, P2-201

## Goal
P2-200 follows a valid path through the solution's own calls, up to four deep and through a sealed type's override,
and cleared the two of its eight false EQ006 that route reaches (Duplicati's `LinuxSnapshot` #36 and #44). The other
six (P2-200's Notes have each one's trace) need something no single route gives:
- a local reassigned after its declaration: Duplicati's `RecoveryTool.Index::WriteIndex` (#43), whose only caller
  passes a local whose last write is `Path.GetFullPath` and whose first is an `out` argument; Duplicati's
  `IndexHtmlHandler::Process` (#30), where `Directory.Exists(path)` guards `path` only after the locals `html` and `htm`
  were made from it, and `File.OpenRead(... ? html : htm)` reads `htm`;
- the only caller of a private method through a partial validator: Git Extensions' `FileAssociatedIconProvider::CreateTempFile`
  (#38), whose caller passes `Path.GetFileName` of a path no rule shows valid, which on .NET Framework rejects what
  the flagged `Path.Combine` does;
- a field filled lazily: Git Extensions' `ConfigFileSettings::CreateLocal` (P2-047's row 52, `GitModule.GitCommonDirectory`
  cached under a lock) and `AppSettings::get_AvatarImageCachePath()` (row 62, a static `Lazy<string?>` with a private
  setter);
- a `foreach` variable over `Directory.GetFiles(...)` in a method that also reads a get-only property set from a local
  function: Git Extensions' `GitModuleTestHelper::Dispose()` (#43).

When done, the largest of these is followed, or `equiv-adr` records why none is.

## Spec references
`docs/tickets/done/P2-200-*.md` and `P2-188-*.md`, the `validPaths` header of `runtime-changes.json`, ADR 0019 and its
P2-200 clarification.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each of the six is assigned the route that clears it, and the route followed has its soundness argument in Notes.
2. Rerun the pair or pairs it touches in compare mode quick; Notes give the six procedures' new results and the
   path-row EQ006 count next to P2-200's (60, 98, 16).

## Tests
Precondition unit tests for the route followed, and one where the path really comes from an argument and stays
flagged.

## Size guard
A call graph over the solution, or a reaching-definitions analysis beyond one method's locals: stop and route it
through `equiv-adr`.

## Out of scope
Device-path prefixes (P2-130's undetermined four).

## Notes
- Filed 2026-10-15 by P2-200 (criterion 1).
- Renumbered from P2-202 on 2026-10-15 by P2-192: P2-184 filed another P2-202 first, and `check-ticket-ids.sh` failed.
- Criterion 1, the six by route, from the legacy checkouts (`.corpus/repos/*@beaf03562fdc`, `@3f4ed21998af`). For each,
  what clears it, since a route that leaves another flagged call in the procedure clears nothing:

  | Pair | # | Procedure | Route that clears it | Owner |
  |---|---|---|---|---|
  | Duplicati | 43 | `RecoveryTool.Index::WriteIndex(...)` | the calls of a private method, and the last write of a reassigned local: the one call passes `ixfile`, last written by `ixfile = Path.GetFullPath(ixfile)` after an `out` argument and an `if` | this ticket |
  | Duplicati | 30 | `IndexHtmlHandler::Process(...)` | a partial validator: the two `Path.Combine(path, ...)` read `GetPath`'s `Path.Combine`, valid for another `Combine` and not for a `FileStream`; and `File.OpenRead(File.Exists(html) ? html : htm)` needs `Directory.Exists(path)` to reach `htm`, declared from `path` before the guard | P2-227 |
  | Git Extensions | 38 | `FileAssociatedIconProvider::CreateTempFile(string)` | the calls of a private method (this ticket) and a partial validator: the one call passes `Path.GetFileName(fullPath)`, `fullPath` valid by no rule, to the flagged `Path.Combine` | P2-227 |
  | Git Extensions | row 52 | `ConfigFileSettings::CreateLocal(GitModule,ConfigFileSettings,SettingLevel,bool)` | a field filled on first use: `GitCommonDirectory` returns `_gitCommonDirectory`, written only in that getter from a local kept only under `Directory.Exists` and otherwise set from `GetGitDirectory()`, which reads `WorkingDir` (P2-208's #55) | P2-228 |
  | Git Extensions | row 62 | `AppSettings::get_AvatarImageCachePath()` | none: `ApplicationDataPath` is a `Lazy<string?>` property with a private setter the nested `TestAccessor` also sets, and its factory ends in `String.Replace`, which can move a colon off a drive letter, so no sound rule reaches it | recorded in P2-228, not followed |
  | Git Extensions | 43 | `GitModuleTestHelper::Dispose()` | a listing of a valid directory: a `foreach` over `Directory.GetFiles(TemporaryPath, ...)`, after `TemporaryPath` (P2-208's #51) | P2-229 |

  The two OpenRA results `docs/runs/2026-10-15-precision-verdict.md` assigned here need other routes too:
  `GameSaveBrowserLogic::Select(string)` (#10) is private, but its calls pass `null` and a widget's `ItemKey` filled from
  `Directory.GetFiles` (P2-229), and `Game::InitializeSettings(Arguments)` (#15) reads `Platform.SupportDir`, static fields
  filled on first use with a separator appended (P2-228).
- Decision: the route followed is the calls of a private method, with the reaching write of a local that one of them
  needs. By the ticket's grouping, a reassigned local (#43, #30) and a lazily filled field (rows 52, 62) are two each;
  the field route clears neither alone (row 52 also needs a reassigned local under a guard and P2-208, row 62 nothing
  sound), and the local route clears #43 only with the caller route. The caller route is also what #38 needs first.
  Both stay inside the size guard: the reaching write reads one method's statements, and the caller route scans one
  type's source for one method's name.
- Decision: the last write is found by statements, not a control-flow graph. From the read, each enclosing block is
  searched backwards for the last statement that writes or declares the variable; it counts only when it is
  `x = value;` or a declaration with a value and nothing from it to the read writes the variable again (the read's own
  `x = f(x);` is allowed: its write runs after the read). A branch, a loop or a `try` that writes it is not read
  through, so a loop that writes the variable after the read, in its next turn, is caught by the statement that holds
  the read.
- Decision: a variable is read this way only when every write of it in the method is a plain assignment or an `out`
  argument, none in a lambda or local function, the method has no `goto`, the variable is not a `ref` local or a
  `ref`/`out`/`in` parameter, and a parameter belongs to the method whose body is read (a primary constructor's
  parameter is written by other members). A read in a lambda or local function is not followed. An `out` argument
  counts only to a method that returns no `ref` and has no ref struct in its return type, parameters or type: under
  C# 10's rules an `out` parameter can be returned by `ref`, and .NET Framework's reference assemblies carry no
  `RefSafetyRules`, so `ScopedKind` cannot tell.
- Decision: the caller route reads a by-value parameter that nothing assigns, of a private ordinary method (not a
  constructor, local function, lambda or accessor). Every name in the type's declarations that spells the method's name
  is bound: another symbol is skipped, the method itself must be what an invocation calls, alone or after `.` or `?.`,
  and anything else (a method group, `nameof`, a `dynamic` member name, which binds to no symbol) makes the parameter
  not followed. An extension method called as an instance method is matched through `ReducedFrom`. There must be at
  least one call, each counts as a step toward P2-200's bound of four, and each argument is read in its caller with the
  existing rules. A method named `Add`, `AppendLiteral` or `AppendFormatted` is not read: the compiler calls those from
  a collection initializer or an interpolated string handler, with no name in the source.
- Decision: existing tests move. A local declared without a value and assigned later (`string d; d = ...;`) and the
  chain `var a = ...; var b = a; a = b;` were flagged as the limits of P2-074's local rule; both are now read, and a
  loop that writes the two in turn is the new limit.
- Criterion 1, soundness. The reaching write: a statement of a block runs only after every statement before it in
  that block completed, so the value a variable holds at a read is what the last statement before it that wrote it
  left, when nothing between, and nothing in the read's own statement before the read, writes it. What could write it
  unseen is ruled out by the preconditions above: an alias (a `ref` local, a `ref` argument a callee keeps, a `ref`
  parameter's caller), a lambda or local function that runs between two statements, a primary constructor's parameter
  that another member writes, and a `goto` into the statements between. The caller route: the language lets only the
  source of a private method's type, partial declarations and nested types included, name the method, and a by-value
  parameter that nothing assigns holds what the call passed, so the parameter holds one of the values the calls pass,
  each read in its caller. Each side is read with its own callers; a caller whose argument is valid only because a
  validator throws on .NET Framework fires that validator's row in the caller, and two sides whose callers pass
  different values have a caller pair that is not Equivalent. ADR 0019's P2-207 clarification records this. Known gaps,
  all on the side of not flagging, as P2-201's are: reflection and `UnsafeAccessor`, which call a private method from
  elsewhere; a call a source generator adds that did not run (P2-176); `__makeref` and unsafe code, which write a local
  through a `TypedReference` or a pointer; and the compiler's own calls by a name the source does not write beyond the
  three listed.
- `equiv-adr`, bar test, first row: the caller route makes a private method's verdict rest on the values its type's
  calls pass, which ADR 0019 decides for callees and not for callers. Recorded as a dated Clarification on ADR 0019, as
  P2-200's chain of callees was, and VERIFICATION-MODEL section 3's "a path that came from a caller is not followed" now
  names the two rules. Not a new ADR: no verdict, rule id or SARIF field changes, and P2-201 already reads a field's
  values from its own type's writers without one.
- Found in passing: P2-074's guard rule trusts a guard on a primary constructor's parameter across a call that rewrites
  it (a unit probe confirmed it on `main`); no corpus pair has a primary constructor. Filed as P2-230, not fixed here.
- Decision: the before runs are at `main` (`068d8d79`), from a detached worktree at `D:\eq207` (P2-200's path-length
  note); this branch ran at `b22b4155`. Both used the `.corpus` of the `precision-audit-large-pairs` worktree, its
  checkouts and restores linked in by junction. Compare mode quick, `--jobs 4`, one run at a time. Every run exited 1
  (a Divergent result), no project skipped.
- Criterion 2, the six procedures, P2-200's run, then `main`, then this branch:

  | Pair | # | Procedure | P2-200 | `main` | P2-207 |
  |---|---|---|---|---|---|
  | Duplicati | 43 | `RecoveryTool.Index::WriteIndex(ref SortedSet<string>,string)` | EQ006 | EQ006 | Unknown (`opaque`, `rebound-call`) |
  | Duplicati | 30 | `IndexHtmlHandler::Process(...)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | 38 | `FileAssociatedIconProvider::CreateTempFile(string)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | row 52 | `ConfigFileSettings::CreateLocal(GitModule,ConfigFileSettings,SettingLevel,bool)` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | row 62 | `AppSettings::get_AvatarImageCachePath()` | EQ006 | EQ006 | EQ006 |
  | Git Extensions | 43 | `GitModuleTestHelper::Dispose()` | EQ006 | EQ006 | EQ006 |

  #43's `new StreamWriter(file1)` is no longer flagged; its other difference is `sw.WriteLine` bound to
  `TextWriter::WriteLine(string)` on one side and `StreamWriter::WriteLine(string)` on the other, P2-179's
  `rebound-call`. OpenRA's #10 and #15 stay EQ006, as the table above says they must.
- Criterion 2, path-row EQ006 (counted as P2-188 did: an EQ006 whose `reviewGroup` names a row with the `invalidPath`
  precondition) and all EQ006:

  | Pair | Path-row EQ006: P2-200, `main`, P2-207 | All EQ006: P2-200, `main`, P2-207 |
  |---|---|---|
  | `gitextensions-8522` | 60, 55, 54 | 141, 131, 130 |
  | `duplicati-3124` | 98, 95, 94 | 117, 114, 113 |
  | `openra-17989` | 16, 16, 16 | 37, 34, 34 |
  | All three | 174, 166, 164 | 295, 279, 277 |

  `main` has fewer than P2-200's branch; P2-201, P2-217 and the other tickets merged since moved them, and that
  difference is not attributed. Between `main` and this branch every other result of the three pairs is the same. The
  three that changed, all correct by reading the source: #43 above; Git Extensions'
  `GitExtensions.Plugins.Gource.GourcePlugin::DownloadFile(string,string)`, EQ006 to Equivalent (`congruence`), a private
  method whose one call passes `Path.Combine(Path.GetTempPath(), "gource.zip")` to its `File.Create`; and
  `GourcePlugin::UnZipFiles(string,string,bool)`, Unknown (`unaligned-loop`) to Unknown (`opaque`), a private method
  whose one call passes that file and `Path.Combine(downloadDir, "gource")`, so its `File.OpenRead`,
  `Directory.CreateDirectory` and `File.Delete` are no longer flagged, while `Path.Combine(outputFolder, entry.Name)`, a
  zip entry's name, still is. No result became Divergent or Unknown from Equivalent. Run times at `main` and this branch:
  Git Extensions 380 s and 360 s, Duplicati 473 s and 483 s, OpenRA 241 s and 224 s; one run each, so the difference is
  not attributed.
- Decision: no `Release:` footer. A precision fix that changes no surface is a patch (`equiv-release`).
