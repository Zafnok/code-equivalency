# P2-200 Precision bug: a path row fires on a valid path more than one call away, or validated by a partial validator
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-188

## Goal
P2-188 reads a solution callee one call deep, and its one validator is `Path.GetFullPath`. Eight of
the 16 false EQ006 P2-130 adjudicated to it are still EQ006 for reasons a field does not explain
(P2-199 has those):
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
Fields and get-only properties (P2-199). Device-path prefixes (P2-130's undetermined four).

## Notes
- Filed 2026-10-14 by P2-188 (criterion 1).
