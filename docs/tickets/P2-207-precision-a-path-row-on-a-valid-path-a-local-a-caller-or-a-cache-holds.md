# P2-207 Precision bug: a path row fires on a valid path that a reassigned local, a private method's caller or a cached field holds
Status: todo
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
