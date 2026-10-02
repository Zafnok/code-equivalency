# P2-094 `x is not T t` lowers as the negation of its inner pattern
Status: todo
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
