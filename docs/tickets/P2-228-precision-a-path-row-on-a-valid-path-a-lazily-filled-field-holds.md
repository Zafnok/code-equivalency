# P2-228 Precision bug: a path row fires on a valid path that a field filled on first use holds
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-207, P2-208

## Goal
P2-201 reads a `readonly` field or get-only auto-property through its type's constructors. A cache is neither: a
private mutable field that a getter fills the first time it runs. Two false EQ006 reach the call through one (P2-207's
Notes and `docs/runs/2026-10-15-precision-verdict.md` have the traces):
- Git Extensions row 52, `ConfigFileSettings::CreateLocal(GitModule,ConfigFileSettings,SettingLevel,bool)`:
  `GitModule.GitCommonDirectory` returns `_gitCommonDirectory`, written only in that getter, under a lock, from a local
  `dir` that is kept only when `Directory.Exists(dir)` passed and is otherwise reassigned to `GetGitDirectory()`, which
  reads `WorkingDir` (P2-208's #55);
- OpenRA #15, `Game::InitializeSettings(Arguments)`: `Platform.SupportDir` returns static fields that
  `InitializeSupportDir` fills, guarded by a flag, from `Environment.GetFolderPath` and `Path.Combine`, with a separator
  appended (P2-208's trailing separator).

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
