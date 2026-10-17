# P2-231 Precision bug: a path row fires on the result of a member that .NET Framework's own path check already passed
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-207

## Goal
Every path row has the one precondition `invalidPath` (a blank path, 248 characters or more, a control character,
one of `" < > | ? *`, or a stray colon), and the one validator is `Path.GetFullPath`, which on .NET Framework rejects
all of it. Some rows' own .NET Framework check rejects less: `Path.Combine` and `Path.GetFileName` check only `" < > |`
and control characters, so their change reaches no other argument. The result of such a member passed that same
check on .NET Framework, so it is a valid path for another row whose check is the same, and not for a `FileStream`.
Two of P2-207's six need this (P2-207's Notes have each trace):
- Git Extensions #38, `FileAssociatedIconProvider::CreateTempFile(string)`: its only caller passes
  `Path.GetFileName(fullPath)` to the flagged `Path.Combine(Path.GetTempPath(), fileName)`; P2-207 follows the caller,
  and `fullPath` is valid by no rule;
- Duplicati #30, `IndexHtmlHandler::Process(...)`: the two `Path.Combine(path, ...)` calls read `GetPath`'s result,
  a `Path.Combine` of a field set from a constructor parameter; and `File.OpenRead(File.Exists(html) ? html : htm)`
  reads `htm`, valid only because `Directory.Exists(path)` guards it, after `htm = Path.Combine(path, "index.htm")` was
  declared: a guard on a variable reaching a local that a transform made from it, which no rule reads either.

When done, a row whose .NET Framework check is narrower than `invalidPath` says so, the members whose result passed
that check are read as valid for it, and #38 is no longer EQ006, or `equiv-adr` records why not.

## Spec references
`docs/tickets/done/P2-188-*.md` (validators and their soundness argument), `P2-207-*.md`, the `precondition` and
`validPaths` header of `src/Equiv.Core/RuntimeChanges/runtime-changes.json`, VERIFICATION-MODEL section 3.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each path row's .NET Framework check is read from the reference source and recorded in the table; a member is a
   validator for a row only when its own check covers that row's.
2. Rerun `gitextensions-8522` and `duplicati-3124` in compare mode quick; Notes give #38's and #30's results and the
   path-row EQ006 count next to P2-207's.

## Tests
Precondition unit tests: a `Path.Combine` of `Path.GetFileName(p)` is not flagged, a `FileStream` of it is; and one
where the path really comes from an argument and stays flagged.

## Size guard
A precondition per character class across every row: stop and route it through `equiv-adr`.

## Out of scope
Device-path prefixes (P2-130's undetermined four).

## Notes
- Filed 2026-10-16 by P2-207 (criterion 1).
- Criterion 1, each path row's .NET Framework check, read from `microsoft/referencesource` (`mscorlib/system/io/path.cs`,
  `pathinternal.cs`, `file.cs`, `directory.cs`, `filestream.cs`, `streamreader.cs`, `streamwriter.cs`,
  `security/permissions/fileiopermission.cs`, `reflection/assemblyname.cs`; `System/net/.../mail/Attachment.cs`;
  `System.Xml/.../XmlTextWriter.cs`, `XmlReaderSettings.cs`, `XmlResolver.cs`, `XmlDownloadManager.cs`) and recorded as each
  row's `frameworkCheck`:

  | `frameworkCheck` | What it rejects | Rows |
  |---|---|---|
  | `pathCharacters` | `Path.CheckInvalidPathChars`: a control character and `" < > \|`; a device path is not read | `Combine` (2, 3, 4 parts), `GetFileName`, `GetFileNameWithoutExtension` (through `GetFileName`), `GetExtension`, `ChangeExtension` (its path only), `IsPathRooted` |
  | `limitedNormalization` | that, and `NormalizePath(fullCheck: false)`: a blank path, a long one; wildcards and colons pass | `GetDirectoryName`, `GetPathRoot` |
  | `fullPath` | `NormalizePath(fullCheck: true)` and a `FileIOPermission` demand or its emulation: wildcards and a stray colon too | `GetFullPath`; `File.Delete`, `GetAttributes`, `GetLastWriteTime(Utc)` (`GetFullPathInternal` and `QuickDemand`); `File.Create`, `OpenRead`, `ReadAllBytes`, `ReadAllText`, `ReadAllLines`, `ReadLines`, `StreamReader`, `StreamWriter`, `Attachment`, `XmlTextWriter` (a `FileStream`); `Directory.CreateDirectory`, `Delete`; `AssemblyName.GetAssemblyName` (a `PathDiscovery` demand); `XmlReader.Create` (`GetFullPath` of a relative path, then a `FileStream`); `SetCurrentDirectory` (below) |

  `SetCurrentDirectory` calls `GetFullPathInternal` and demands no `FileIOPermission`, so its own check may pass a wildcard
  or a colon; `LongPathHelper`, which does its character check, is not in the published reference source. It is recorded
  as `fullPath`: for a row, a check recorded wider than it is only fires on more calls, so the record is sound either way.
- Decision: three named checks in one order, not a set of character classes per row (the size guard). Only
  `pathCharacters` is read as narrower than `invalidPath`: a row's precondition still names every class, and an operand
  that passed the character check does not meet it only on a row whose check is that one. The other two are recorded
  for criterion 1 and read as `invalidPath`.
- Decision: the members whose result passed the character check are a list of their own, `characterValidators`, as P2-188
  made `validators` one: `Combine(string,` (its 2, 3 and 4 parts; `Combine(string[])` has no row), `GetFileName`,
  `GetFileNameWithoutExtension`, `GetExtension`, `GetDirectoryName` and `GetPathRoot`. Each checks every path it reads at
  least as `pathCharacters` does and returns a part or a join of them. `GetDirectoryName`'s and `GetPathRoot`'s results
  are shown to pass only the character check (nothing in their source rules out a blank part of the path), so they are
  character validators and not `limitedNormalization` ones. `ChangeExtension` is none: it checks its path and not its
  extension, which it appends.
- Decision: a third mode of the frontend's rules, beside a valid path and a valid path or the empty string (P2-232). An
  operand passed the character check when it is a valid path, the empty string, or the result of a character validator,
  read through every other rule (locals, callees, a private method's callers, stored fields) as a valid path is. A
  transform's arguments are still read as valid paths; a constant is still read as `invalidPath` reads it. The result
  is `CallArgument.ValidPathCharacters`.
- Soundness, as P2-188's for a validator. On .NET Framework a character validator throws `ArgumentException` for any path
  it reads that has a control character or one of `" < > |` (`CheckInvalidPathChars`), so what it returns is a part or a
  join of paths without them, and a later row whose check is that same one cannot reach its change. On another runtime
  the validator returns such a path, and then its own row fires on its argument, in the method that calls it: every
  character validator is a path row (`EveryCharacterValidatorIsAPathRowWithEveryPathRowsChange`), and every path row has
  the same `changedIn`, so any pair that crosses the row being decided crosses the validator's too. A row whose check is
  wider still fires on such a result: a `?`, a `*`, a stray colon, a blank path or a long one passes the character check.
  Known gap, on the side of not flagging and a device-path case (out of scope, as P2-188's `\\?` was): .NET Framework's
  `HasIllegalCharacters` reads no device path (`\\?\`, `\\.\`), and `GetFileName`, `GetFileNameWithoutExtension` and
  `GetExtension` of one return its last part without the prefix, so a control character or one of `" < > |` there reaches
  a later character check. `Combine`, `GetDirectoryName` and `GetPathRoot` keep the prefix (a rooted device part is the
  whole result, and a device first part starts it), and no row's character check reads a device path.
- `equiv-adr` bar test: not a new ADR. No verdict, rule id, SARIF field or component boundary changes; the table gains a
  row field and a list read only by this repo, as P2-232's `emptyPathUnchanged` and `separators` did, and the soundness
  argument is P2-188's for a validator. VERIFICATION-MODEL section 3 names the rule.
- Decision: no `Release:` footer. A precision fix that changes no surface is a patch (`equiv-release`).
- Decision: the before runs are at `main` (`ea498160`) from a detached worktree at `D:\eq231`; this branch ran at
  `6de3c745`. Both used the `.corpus` of the `precision-audit-large-pairs` worktree by junction, `corpus.ps1 -Env` set,
  compare mode quick, `--jobs 4`, one run of this session at a time. Every run exited 1 (a Divergent result) and loaded
  every project (91 and 103). Another session started its own `gitextensions-8522` run in the same `.corpus` at 02:58,
  after this branch's run had loaded; it overlapped this branch's Git Extensions run and `main`'s Duplicati run, so the
  run times are not attributed.
- Criterion 2, the two procedures, P2-207's run, then `main`, then this branch:

  | Pair | # | Procedure | P2-207 | `main` | P2-231 |
  |---|---|---|---|---|---|
  | Git Extensions | 38 | `GitCommands.FileAssociatedIconProvider::CreateTempFile(string)` | EQ006 | EQ006 | Unknown (`opaque`, `rebound-call`) |
  | Duplicati | 30 | `Duplicati.Server.WebServer.IndexHtmlHandler::Process(...)` | EQ006 | EQ006 | Unknown (`abstraction`, `String.EndsWith`) |

  #38's only call passes `Path.GetFileName(fullPath)`, so its `Path.Combine(Path.GetTempPath(), fileName)` no longer fires;
  what is left is `_fileSystem.File.WriteAllText` bound to two versions of `System.IO.Abstractions`, P2-179's
  `rebound-call`. #30's two `Path.Combine(path, ...)` read `GetPath`'s `Path.Combine` and no longer fire;
  `File.OpenRead(File.Exists(html) ? html : htm)` still does (`runtimeChangedCall` is `File.OpenRead(string)`), and the
  divergence the solver found also depends on `String.EndsWith`, so the result is an `abstraction` Unknown rather than
  EQ006. `htm` is valid only through `Directory.Exists(path)`, a guard on the variable it was made from before the guard
  ran, which no rule reads: filed as P2-236, as no open ticket owned it.
- Criterion 2, path-row EQ006 (counted as P2-188 did: an EQ006 whose `reviewGroup` names a row with the `invalidPath`
  precondition) and all EQ006:

  | Pair | Path-row EQ006: P2-207, `main`, P2-231 | All EQ006: P2-207, `main`, P2-231 | Run time: `main`, P2-231 |
  |---|---|---|---|
  | `gitextensions-8522` | 54, 47, 46 | 130, 123, 122 | 410 s, 372 s |
  | `duplicati-3124` | 94, 91, 89 | 113, 110, 108 | 533 s, 544 s |

  `main` has fewer than P2-207; P2-208, P2-232, P2-218 and P2-220 merged since. Between `main` and this branch every other
  result of the two pairs is the same, and the five that changed are each correct by reading the source: #38 and #30
  above, and three Duplicati tests whose `Path.Combine(this.DATAFOLDER, <constant>)` reads a `readonly` field initialized
  to `Path.Combine(BASEFOLDER, "backup-data")`: `RepairHandlerTests::SetUp()` (EQ006 to Equivalent),
  `Issue1723::RunCommands()` and `ProblematicPathTests::ExcludeProblematicPaths()` (Unknown, `timeout`, to Equivalent:
  with the row gone, the quick pass decides the query). No result became Divergent, or Unknown from Equivalent.
