# P2-243 Precision: a test that asserts its own runtime's `FileSystemWatcher.Filter` default is EQ002
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-237

## Goal
`gitextensions-8522`'s `GitCommandsTests.Settings.FileSettingsCacheTests::ctor_FileWatcher_Filter_should_be_default_if_invalid_dir(string)`
is EQ002 at `de93b9c7`. The migration changed the constant the test compares `FileSystemWatcher.Filter` with, to the
default .NET Core gives the property (.NET Framework's default differs), so each side asserts its own runtime's default
and both pass: P2-237 ran it on both runtimes with `probe`, and both returned. The solver sees one uninterpreted getter
compared against two different constants. P2-130 and P2-212 had it undetermined; P2-237 counts it a false positive.

When done, the getter's default is a runtime-dependent value the model knows per side, so the pair is Equivalent, and
the same change on one side only stays Divergent.

## Spec references
VERIFICATION-MODEL section 3 ("Runtime-changed APIs"), `runtime-changes.json`, ADR 0040 decision 3,
`docs/runs/2026-10-17-precision-execute-verdict.md` (Git Extensions row 21).

## Acceptance criteria (all must hold; nothing beyond them)
1. A newly constructed `FileSystemWatcher`'s `Filter` reads as each side's runtime default, from a runtime-changes or
   catalogue entry with its source, and the sampled test is Equivalent.
2. A body that compares the default against the other runtime's value on one side only stays Divergent.
3. The decision on where per-runtime defaults live (a new row kind, or the existing table with a value per runtime) is a
   `Decision:` line in Notes, through `equiv-decide`.

## Tests
Unit tests on a lowered pair of each shape; a Verify snapshot of a `samples/` pair with both shapes.

## Size guard
If a per-runtime value needs a new IR construct, stop and route it through `equiv-adr`.

## Out of scope
Other properties whose default changed, unless the same entry kind covers them for free; list any you find in Notes.

## Notes
- Filed 2026-10-17 by P2-237.
