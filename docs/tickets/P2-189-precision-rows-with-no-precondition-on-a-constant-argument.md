# P2-189 Precision bug: the `Split` TrimEntries, `GetFolderPath` and `DataFormats.GetFormat` rows fire on a constant argument that cannot reach their change
Status: in-progress
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-073 (done) gave four row kinds a precondition on a constant argument. P2-130's adjudication
(`docs/runs/2026-10-14-migrations-verdict.md`) found 11 false EQ006 on rows that have none, on all
three migration pairs:
- `System.String::Split(char[],System.StringSplitOptions)` and `(string[],System.StringSplitOptions)`
  (TrimEntries, `changedIn` net5.0): 7 calls with a constant `None` or `RemoveEmptyEntries`; only an
  options value with bit 2 set reaches the change (`gitextensions-8522` 2, `duplicati-3124` 2,
  `openra-17989` 3);
- `System.Environment::GetFolderPath(System.Environment.SpecialFolder)` (a missing folder returns
  empty): 3 calls with `CommonApplicationData`, `ProgramFiles` and `UserProfile`, folders every Windows
  install has (`gitextensions-8522`);
- `System.Windows.Forms.DataFormats::GetFormat(string`: 1 call with `DataFormats.UnicodeText`, a
  framework `static readonly` field holding a valid name, not a C# constant (`gitextensions-8522`).

When done, each of these rows has a precondition its constant arguments can make false.

## Spec references
`docs/tickets/done/P2-073-*.md`, `src/Equiv.Core/RuntimeChanges/runtime-changes.json`,
`RowPrecondition`, ADR 0040.

## Acceptance criteria (all must hold; nothing beyond them)
1. Done by P2-181: the two `Split` rows have the precondition `trimEntries`, met by any options but
   `None` and `RemoveEmptyEntries`.
2. The `GetFolderPath` row does not fire on a constant folder from a documented always-present list,
   recorded with its source.
3. The `GetFormat` row does not fire on a `DataFormats` field that holds a valid format name, decided
   with `equiv-decide` (a `static readonly` framework field read as its documented value, or a listed
   set of fields).
4. Rerun the three migration pairs in compare mode quick; Notes give the 11 procedures' new results.

## Tests
Precondition table tests per row; a call with a non-constant options value stays EQ006.

## Size guard
New rows: stop.

## Out of scope
Path rows (P2-188).

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
- 2026-10-14: P2-181 gave the two `Split` rows their precondition (criterion 1), since its count of the
  unedited bodies a runtime rule keeps out of congruence found them in 75 of the 1,004. Criterion 4's
  rerun still gives the 7 `Split` procedures' results.
- Decision: the `GetFolderPath` row's always-present list -> a new precondition `absentFolder`, met by any folder
  but `System` and the nine .NET's own `GetFolderPathCore` falls back to a Windows environment variable for
  (`ApplicationData`, `CommonApplicationData`, `LocalApplicationData`, `ProgramFiles`, `ProgramFilesX86`,
  `CommonProgramFiles`, `CommonProgramFilesX86`, `UserProfile`, `Windows`). Source:
  dotnet/runtime `src/libraries/System.Private.CoreLib/src/System/Environment.Windows.cs`, whose comment on `System` says it
  "assumes the system directory always exists"; the others are the folders Windows names in an environment variable for
  every session (dotnet/runtime#109673 added the fallback in .NET 10). Alternatives: the KNOWNFOLDERID page's `FIXED`
  category (it says where a folder is, not that it exists), a measured list (one machine cannot show every install).
  Rule: 1.
- Decision: a `DataFormats` field as a `GetFormat` argument -> a top-level `fieldValues` map in `runtime-changes.json`
  from a framework `static readonly` field to its documented value, which `CallArguments` reads as a constant; a new
  precondition `blankFormat` reads the `format` argument and is met by null and a blank string (the breaking-change page's
  condition). The map lists the 20 `DataFormats` fields whose value is the same on .NET Framework and .NET (dotnet/winforms
  `DataFormatNames.cs`); `Serializable` is left out, as .NET Framework prefixes it with the Windows Forms version. A listed
  field matches by identity alone, as a row's member does. Alternatives: a flag on `CallArgument` for a known non-blank
  field (one more flag no other precondition reads), any `static readonly` field of a reference assembly read as
  non-constant-but-valid (no value to read). Rule: 3.
