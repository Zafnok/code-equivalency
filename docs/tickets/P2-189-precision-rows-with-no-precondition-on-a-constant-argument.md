# P2-189 Precision bug: the `Split` TrimEntries, `GetFolderPath` and `DataFormats.GetFormat` rows fire on a constant argument that cannot reach their change
Status: todo
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
1. The two `Split` rows require an options argument that may have bit 2 set.
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
