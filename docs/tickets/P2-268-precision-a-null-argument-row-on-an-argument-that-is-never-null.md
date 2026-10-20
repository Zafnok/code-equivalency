# P2-268 Precision bug: a row that differs only for a `null` argument fires where the argument is an array literal or a boxed struct
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
Some runtime rows differ only in the exception a `null` argument raises (`Font.FromLogFont(null)`,
`AddRange(null)`). P2-126 makes a string literal never `null`; nothing does the same for an array creation, a collection
expression, or a boxed value type. P2-247 (`docs/runs/2026-10-20-precision-execute-verdict-2.md`, appendix C) found two
Git Extensions exposures, both false positives: `W05` (the argument is a boxed `LOGFONT` out-struct) and `U06` (an array
literal). When done, an argument that is an array creation, a collection expression, a `new` expression or a boxing
conversion is non-null, and a row whose precondition is a `null` argument does not fire on it.

## Spec references
`src/Equiv.Core/RuntimeChanges/runtime-changes.json`, `RowPreconditions`, ticket P2-126,
`docs/runs/2026-10-20-precision-execute-verdict-2.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Unit tests: `AddRange(new[] { a, b })` and `Font.FromLogFont((object)lf)` fire no null-argument row;
   `AddRange(items)` of a parameter still does.
2. `gitextensions-8522` in `full`: both results are given with their result now, in Notes.

## Notes
- Filed 2026-10-20 by P2-247.
