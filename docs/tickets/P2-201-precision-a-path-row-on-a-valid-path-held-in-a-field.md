# P2-201 Precision bug: a path row fires on a valid path that a field or get-only property holds
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-188

## Goal
P2-188 (Notes) follows a valid path through one of the solution's own methods; a field is still not
followed. Four of the 16 false EQ006 P2-130 adjudicated to P2-188 reach the call through a field or a
get-only property:
- `GitCommandsTests.Git.Tag.GitTagControllerTest::Setup()` (P2-047's row 61): a `readonly` field whose
  initializer is `TestContext.CurrentContext.TestDirectory`, a listed source;
- `GitUITests...FormFileHistoryControllerTests::TryGetExactPathName_should_check_if_path_matches_case(...)`
  (`gitextensions-8522` #51): a get-only auto-property the constructor sets from a local function that
  returns `Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())`;
- `GitUI.CommandsDialogs.FormResolveConflicts::Mergetool_Click(...)` (#55): `GitModule.WorkingDir`, set
  in the constructor through the solution's `NormalizePath` extension, which ends in `Path.GetFullPath`;
- `OpenRA.FileFormats.ReplayMetadata::RenameFile(string)` (#8): a property with a private setter whose
  writers store null or a path a `FileStream` or `File.Move` accepted.

Over all 181 path-row EQ006 of the three migration pairs, P2-188's census counts 53 whose flagged
argument is a field or property (33 of the method's own type, 20 of another object), the second
largest shape after a caller's parameter. When done, a `readonly` field or get-only auto-property
whose every write (initializer, and assignment in a constructor of its type) is a valid path by
P2-188's rules is one.

## Spec references
`docs/tickets/done/P2-188-*.md` and `P2-074-*.md`, the `validPaths` header of
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`, ADR 0019.

## Acceptance criteria (all must hold; nothing beyond them)
1. A read of such a field or property is a valid path, with the soundness argument in Notes (what
   writes a `readonly` field outside its type's constructors, a `ref` to it, reflection).
2. Rerun the three migration pairs in compare mode quick; Notes give the four procedures' new results
   and the path-row EQ006 count next to P2-188's (59, 100, 16).

## Tests
Unit tests: a `readonly` field set from a source stays unflagged, one set from a constructor parameter
is flagged, a mutable field is flagged.

## Size guard
A writer outside the field's own type, or a property with a setter: stop, that is not this ticket.

## Out of scope
OpenRA #8's private-set property (its writers are in other methods), unless criterion 1's rule
reaches it without more.

## Notes
- Filed 2026-10-14 by P2-188 (criterion 1).
