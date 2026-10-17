# P2-228 Precision bug: a path row fires on a valid path that a field filled on first use holds
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-207, P2-208, P2-232

## Goal
P2-201 reads a `readonly` field or get-only auto-property through its type's constructors. A cache is neither: a
private mutable field that a getter fills the first time it runs. Two false EQ006 reach the call through one (P2-207's
Notes and `docs/runs/2026-10-15-precision-verdict.md` have the traces):
- Git Extensions row 52, `ConfigFileSettings::CreateLocal(GitModule,ConfigFileSettings,SettingLevel,bool)`:
  `GitModule.GitCommonDirectory` returns `_gitCommonDirectory`, written only in that getter, under a lock, from a local
  `dir` that is kept only when `Directory.Exists(dir)` passed and is otherwise reassigned to `GetGitDirectory()`, which
  reads `WorkingDir` (P2-232's #55, filed by P2-208);
- OpenRA #15, `Game::InitializeSettings(Arguments)`: `Platform.SupportDir` returns static fields that
  `InitializeSupportDir` fills, guarded by a flag, from `Environment.GetFolderPath` and `Path.Combine`, with a separator
  appended (P2-232's trailing separator).

Git Extensions row 62, `AppSettings::get_AvatarImageCachePath()`, is the third lazily filled value P2-207 was given,
and no rule here reaches it: `ApplicationDataPath` is a `Lazy<string?>` property with a private setter that the
nested `TestAccessor` also sets, and its factory ends in `String.Replace`, which can move a colon off a drive letter.
Record it as not followed.

When done, a private field of a class whose every write in its own type is a valid path by the existing rules is read
as one, or `equiv-adr` records why not.

## Spec references
`docs/tickets/done/P2-201-*.md` and `P2-207-*.md`, the `validPaths` header of `runtime-changes.json`, ADR 0019 and its
clarifications.

## Acceptance criteria (all must hold; nothing beyond them)
1. The rule's soundness argument is in Notes: what writes a private field outside its type (reflection, a `ref` to it
   that escapes), and what a read before the first write sees (null).
2. Rerun the pairs it touches in compare mode quick; Notes give the two procedures' new results and the path-row EQ006
   count next to P2-207's.

## Tests
Unit tests: a private field written only from a source stays unflagged, one also written from a parameter is flagged,
one whose `ref` is passed out is flagged.

## Size guard
A write of the field outside its own type, or a read across threads beyond what the language's memory model promises
for a reference: stop, that is not this ticket.

## Out of scope
Row 62's `Lazy<T>` factory.

## Notes
- Filed 2026-10-16 by P2-207 (criterion 1).
- Decision: the member followed is a private field of a class that is not `readonly` and whose type is a reference type
  (`ValidPaths.Stored`). Its values are its initializer and every write in the declarations of its type, partial
  declarations and nested types included (`ValidPaths.WritesInType`): each name there that spells the field and binds to
  it, or to no symbol, is read as `LocalValues.Write` reads a reference, so a write whose value is not known, or a name
  that is not a plain read or write of the field (a `dynamic` member name, which the runtime binder can resolve to it from
  inside the type), makes it not valid. As for P2-201, there must be at least one value, each is read by the existing
  rules in the body that gives it, and the read is a step toward the bound of four. A getter that fills the field on
  first use is one such body; nothing about locks or flags is read, since every write must give a valid path anyway.
- Decision: a value-type field is not followed. Before its first write it holds its default (`'\0'` for a `char`), which a
  path part must not be, while a reference-type field holds null, argued below.
- Decision: `LocalValues.Write` now also counts as a write of a value not known a `ref` return of the variable, its address
  (`&f`) and an operation with no bound kind around it (`__makeref`): through each, code the bound tree does not show can
  write it, from outside the type too. For a local or parameter this only makes the existing rules stricter.
- Decision: a member of a class with a `StructLayout` attribute is not read. With `LayoutKind.Explicit`, two reference fields
  at one `FieldOffset` are one slot and a write of either writes both; any layout is left out, as only an explicit one can
  overlay fields. Found writing this argument, and it holds for P2-201's `readonly` field too (a unit test, failing at
  `main`, shows a `readonly` field read as valid while another field laid over it is written `"a|b"`): fixed here, since
  the one check in `Stored` covers both rules.
- Decision: existing tests move. P2-201's mutable static field and P2-218's mutable field, each private and written only
  with a valid path, are now valid paths; both cases keep their intent as `internal` fields. `ValidPaths.Callee` now
  returns a name itself when it is the left part of a member access (`f.Length`); P2-207's `CallsOf` only ever passes it
  a method's name, where that does not occur.
- Criterion 1, soundness. A private field can be named only in its own type's source (its partial declarations and nested
  types; CS0122 elsewhere), and the rule reads every name there that spells it. Each write it reads is a plain or
  compound assignment whose value the rules show valid; every other write, and every way to take a `ref` to the field
  (`ref`, `out` and `in` arguments, `ref` locals, `ref` reassignment, `ref` conditionals, `ref` returns, `&f`,
  `__makeref`), has a value not known and makes the field not valid, so no `ref` to it can leave the type and be written
  elsewhere. So the field only ever holds null or a value one of those writes stored. Across threads the rule needs only
  what the runtime promises for a reference: ECMA-335 Partition I 12.6.6 makes a read or write of an object reference
  atomic, so a read sees null or one stored reference, never a mix, and the runtime's memory model (dotnet/runtime
  `docs/design/specs/Memory-model.md`, object assignment and data-dependent reads) has a reader that sees a reference see
  the string built before it was stored; strings do not change after that. Nothing about which write a read sees is
  assumed. A read before the first write sees null, the default of a reference-type field. Null reaches no `invalidPath`
  row's change: each of the 31 rows' members checks for null before the character validation the row describes, with the
  same outcome on .NET Framework and .NET, documented the same for both: `ArgumentNullException` from `Path.Combine` (all
  three), `Path.GetFullPath`, `File.Create`, `Delete`, `GetAttributes`, `GetLastWriteTime`, `GetLastWriteTimeUtc`,
  `OpenRead`, `ReadAllBytes`, `ReadAllLines`, `ReadAllText`, `ReadLines`, `Directory.CreateDirectory`, `Delete`,
  `SetCurrentDirectory`, the `StreamReader` and `StreamWriter` constructors, `Attachment(string)`,
  `AssemblyName.GetAssemblyName`, `XmlReader.Create(string,XmlReaderSettings)` and `XmlTextWriter(string,Encoding)` (through
  `FileStream`); null from `Path.GetDirectoryName`, `GetFileName`, `GetFileNameWithoutExtension`, `GetExtension`,
  `GetPathRoot` and `ChangeExtension`; false from `Path.IsPathRooted`. Joined to a string, null is the empty string, as the
  null `Path.GetDirectoryName` returns for a root, a transform's result the rules have read as a valid path since P2-074,
  already is. Known gaps, all on the side of not flagging as P2-201's and P2-207's are: reflection (`FieldInfo.SetValue`
  writes a private field on every runtime), serializers that set private fields, `UnsafeAccessor` (.NET 8 and later,
  which returns a writable `ref` to a private field from anywhere), `Unsafe.As` and other unsafe code; a part of the type a
  source generator adds that did not run (P2-176); and a constant of white space only joined to a null read is blank, as
  joined to `Path.GetDirectoryName`'s null for a root it already is under P2-208's join rule.
- `equiv-adr`, bar test: no ADR, as P2-201 needed none. The rule reads a member's values from its own type's writes, as
  P2-201's does; no verdict, rule id or SARIF field changes, and the `validPaths` header of `runtime-changes.json`, which
  states these rules, has the new one.
- Criterion 1, the three lazily filled values, from the legacy and modern checkouts (`gitextensions@3f4ed21998af` and
  `@5190ba5c1a5f`, `openra@fef7a018f2b2` and `@5e74e58b2204`, each pair the same in the code read here):

  | Pair | # | Procedure | What the rule now reads | What still stands between it and a valid path |
  |---|---|---|---|---|
  | Git Extensions | row 52 | `ConfigFileSettings::CreateLocal(GitModule,ConfigFileSettings,SettingLevel,bool)` | `Path.Combine(module.GitCommonDirectory, "config")`: `GitModule` is sealed, its getter returns `_gitCommonDirectory`, a private field written once, in that getter, as `_gitCommonDirectory = dir;` | `dir` is declared from the trimmed output of `git rev-parse` and reassigned inside an `if` when `Directory.Exists(dir)` failed, so neither P2-207's last write (a plain statement) nor a guard (nothing assigns the variable) reads it; and the reassigned value, `GetGitDirectory()`, returns `GitDirectoryResolverInstance.Resolve(WorkingDir)`, an interface call on a `static readonly IGitDirectoryResolver` whose body no rule knows (P2-200), and whose implementation returns a path read from a `.git` file (P2-218's #36). Not followed. |
  | OpenRA | 15 | `Game::InitializeSettings(Arguments)` | `Path.Combine(Platform.SupportDir, "settings.yaml")`: `SupportDir` calls `GetSupportDir(SupportDirType.User)`, whose `switch` returns four private static fields, each now read through its writes in `Platform` | `modernUserSupportPath` is written on Linux from `Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")`, which can hold any character, and `userSupportPath` from it and from `OverrideSupportDir(string path)`'s public parameter, which `Directory.Exists` checks before an `if` appends a separator to it. No sound rule reaches the environment variable. Not followed. |
  | Git Extensions | row 62 | `AppSettings::get_AvatarImageCachePath()` | nothing: `ApplicationDataPath` is a `Lazy<string?>` property with a private setter that the nested `TestAccessor` also sets, and its factory ends in `String.Replace`, which can move a colon off a drive letter | recorded, not followed (Out of scope) |

  Neither of the two clears with this rule alone, as P2-207 expected for row 52; each blocker is a value no sound rule
  shows valid, not a missing route through the field.
