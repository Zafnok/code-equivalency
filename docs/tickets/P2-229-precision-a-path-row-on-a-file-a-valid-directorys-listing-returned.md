# P2-229 Precision bug: a path row fires on a file name that a valid directory's own listing returned
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-208

## Goal
`Directory.GetFiles(dir, ...)` and `Directory.EnumerateFiles(dir, ...)` return paths the file system made under a
directory .NET Framework already accepted, so each element is a valid path when `dir` is. A `foreach` variable over
such a listing is not followed. Two false EQ006 need it (P2-207's Notes and `docs/runs/2026-10-15-precision-verdict.md`
have the traces):
- Git Extensions #43, `GitModuleTestHelper::Dispose()`: `File.GetAttributes(file)` and `File.Delete(file)` in a
  `foreach` over `Directory.GetFiles(TemporaryPath, "*", SearchOption.AllDirectories)`; `TemporaryPath` itself is
  P2-208's #51, so this clears only after it;
- OpenRA #10, `GameSaveBrowserLogic::Select(string)`: a private method whose callers pass `null` (its use is under
  `savePath == null ? ... :`) or a widget's `ItemKey`, set from a `foreach` over `Directory.GetFiles(baseSavePath, ...)`.

When done, an element of a listing of a valid directory is read as a valid path, or `equiv-adr` records why not.

## Spec references
`docs/tickets/done/P2-074-*.md` (sources) and `P2-207-*.md`, the `validPaths` header of `runtime-changes.json`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The listing members read are named, each with why its elements pass .NET Framework's check (a long path, a name
   with a character Windows file names allow and `invalidPath` rejects), in Notes.
2. Rerun the pairs it touches in compare mode quick; Notes give the two procedures' new results and the path-row EQ006
   count next to P2-207's.

## Tests
Unit tests: a `foreach` over `Directory.GetFiles` of a valid path is not flagged, one over a parameter's listing is.

## Size guard
A listing stored in a collection and read back elsewhere: stop, that is not this ticket.

## Out of scope
Device-path prefixes (P2-130's undetermined four).

## Notes
- Filed 2026-10-16 by P2-207 (criterion 1).
