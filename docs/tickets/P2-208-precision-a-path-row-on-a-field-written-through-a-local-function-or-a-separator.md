# P2-208 Precision bug: a path row fires on a valid path a field holds through a local function, a trailing separator or a private setter
Status: todo
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
