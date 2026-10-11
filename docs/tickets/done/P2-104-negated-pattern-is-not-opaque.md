# P2-104 `x is not T t` lowers as the negation of its inner pattern
Status: done (PR #472)
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-058

## Goal
IDE0019 rewrites `T t = x as T; if (t == null)` to `if (x is not T t)`. `IsPattern` lowers type and
declaration patterns, and every other pattern is opaque with reason `switch-pattern`. A negated
pattern is one of the others. On `powershell-19687` that makes 3 of the 12 edited pairs opaque:
`CimAsyncOperation::GetBaseObject`, `CimAsyncOperation::GetReferenceOrReferenceArrayObject` and
`CimNewCimInstance::GetCimInstance`. Lower a negated pattern as `boolnot` of its inner pattern,
when the inner pattern is one that `IsPattern` already lowers.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `IsPattern`, `DeclarationPattern`, `TypePattern`;
`.claude/skills/equiv-extend-ir/SKILL.md`;
`docs/runs/2026-10-02-cleanup-powershell-19687/SUMMARY.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `x is not T`, `x is not T t` and `x is not null` lower without an opaque. The declared local is
   written as `DeclarationPattern` writes it now.
2. A negated pattern over an inner pattern that is still opaque stays opaque, reason `switch-pattern`.
3. `IOPERATION-COVERAGE.md` gains the `NegatedPattern` row.
4. On a rerun of `powershell-19687` the three procedures above no longer carry `switch-pattern`.

## Tests
- `IrLowererTests.ANegatedTypePatternIsTheNegationOfTheTypeTest`
- `IrLowererTests.ANegatedPatternOverAnOpaquePatternStaysOpaque`
- `IrLowererSnapshotTests.NegatedDeclarationPattern`

## Out of scope
`and`, `or`, relational, property, list and recursive patterns.

## Notes
- P2-122 (2026-10-09) lowered the `null` pattern on a reference type, so `x is not null` is the
  negation of a pattern `IsPattern` lowers. On `gitextensions-8522`, 28 of the 30 changed pairs that
  `switch-pattern` alone still keeps opaque hold only negated patterns (27 `not null`, 1 a type).
- Decision: the pattern arms of `IsPattern` moved into one recursive `Test(scrutinee, pattern)` that returns
  null, with nothing emitted, for a pattern it does not lower. A negated pattern asks it for its inner pattern,
  so a negation of a negation and of a constant, relational or discard pattern lower too, and an inner pattern
  that is not lowered leaves the whole test the one `switch-pattern` opaque it was.
- Decision: no row in `docs/VERIFICATION-MODEL.md`. It has no pattern rows; `IOPERATION-COVERAGE.md` is where
  patterns are specified. No IR instruction or encoding is new (`boolnot` exists), so Core and the backend are
  untouched.
- An opaque declaration pattern (`o is not int n`) emits two `switch-pattern` opaques, the test and the value of
  its local, as `o is int n` already did.
- Criterion 4, 2026-10-11, census (`--lower-only`, so no compare mode) of `powershell-19687` at 5f42aeb9, exit 0,
  213 s, 34183 matched pairs, no project skipped. `switch-pattern` is in no changed pair's reason set (3 on
  2026-10-02), and all three named procedures lower. Over the whole pair `switch-pattern` is 66 on each side.
  Decision: the check is one thing, so it is recorded here and not as a `docs/runs/` summary; no scoreboard
  number comes from it.
- Loading PowerShell on this box needs, besides the three steps the 2026-10-02 summary lists,
  `DOTNET_ROLL_FORWARD=LatestMajor` for the repository's resource generator: it targets net8.0, which is not
  installed. Two runs before that step exited 4 and are void.
