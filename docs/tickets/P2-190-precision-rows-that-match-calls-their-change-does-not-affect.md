# P2-190 Precision bug: six runtime-change rows match calls their documented change does not affect
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-075 (done) narrowed two rows to the overloads their change affects. P2-130's adjudication
(`docs/runs/2026-10-14-migrations-verdict.md`) found 13 false EQ006 on all three migration pairs where
a row matches a call its change cannot reach:
- the ICU `System.String::IndexOf(` row matches `IndexOf(char)` (and by prefix the other `char`
  overloads), which compare ordinally on every runtime (1, `gitextensions-8522`); done by P2-181, whose
  `cultureSensitiveText` is never met by a call whose every argument but the receiver is a `char` or an
  `int`;
- the `Environment.OSVersion` row fires where the value is compared with 6.0 or 6.1, a threshold the
  compatibility shim (which reports 6.2 on Windows 8.1 and later) never crosses (2,
  `gitextensions-8522`), or where only `.Platform` is read (1, `openra-17989`);
- the `System.Math::Ceiling(double)` row (-0 against +0) fires on a result converted to `int` at once
  (2, `gitextensions-8522`);
- the `FieldInfo::SetValue(` row (static `readonly` fields) fires on a field from `GetField` or
  `GetFields` with constant flags that include `Instance` and not `Static` (3, `openra-17989`), and once
  behind a Mono-only guard that is false on both runtimes (1, `duplicati-3124`);
- the UTF-8 decoding rows fire on an `Encoding.ASCII` receiver (2, `openra-17989`);
- the `Process::Start(` row (the `UseShellExecute` default) fires on a `ProcessStartInfo` whose
  initializer sets `UseShellExecute` (1, `duplicati-3124`).

When done, each row matches only calls that can reach its change.

## Spec references
`docs/tickets/done/P2-075-*.md` and `P2-073-*.md`, `runtime-changes.json`, `RowPrecondition`, ADR 0040.

## Acceptance criteria (all must hold; nothing beyond them)
1. Each case above is either excluded by the row's match or precondition, with the documentation that
   shows the change cannot be reached, or recorded in Notes as kept, with why.
2. Rerun the three migration pairs in compare mode quick; Notes give the 13 procedures' new results.

## Tests
A precondition or match test per case, and for each a call that does reach the change and stays EQ006.

## Size guard
A data-flow analysis beyond one method: stop.

## Out of scope
Path rows (P2-188), constant-argument preconditions (P2-189).

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
- 2026-10-14: P2-181 excluded the `char` searches from the ICU rows (the first case above).
