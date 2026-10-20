# P2-265 Precision bug: the `Char.IsLetter` row fires where both branches of its test return the same
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
The Unicode-category rows (`Char.IsLetter` and its siblings) fire on any call because the two runtimes' Unicode tables
classify some code points differently. P2-247 (`docs/runs/2026-10-20-precision-execute-verdict-2.md`, appendix C) found
`Duplicati.Library.Backend.File::GetDrive()` an unwitnessed exposure: `IsLetter` of a path root's first character only
chooses whether to build a `DriveInfo`, every Latin-1 letter is classified alike, and for any other first character
`DriveInfo` throws an `ArgumentException` the body catches, so both branches return `null`. A false positive. When done,
the solver decides such a body Equivalent (the branch the row decides converges), or the Notes record why it stays an
exposure.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (the Unicode rows), `docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test: a body that returns `null` from both arms of an `IsLetter` test (one through a caught exception) is not
   an exposure.
2. `duplicati-3124` in `full`: `File::GetDrive()` is given with its result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247. The solver gave no witness (`unknownReason` on the result); the gap may be the caught
  `DriveInfo` exception rather than the row.
