# P2-093 A relational pattern lowers as a comparison
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-049

## Goal
P2-049's sample `cleanup-modern-syntax` rewrites an `if`/`else if` chain on an `int` into a `switch`
expression with relational patterns:

```csharp
// legacy
if (score >= 90) { return 4; } else if (score >= 80) { return 3; } else if (score >= 70) { return 2; } else { return 0; }
// modern
return score switch { >= 90 => 4, >= 80 => 3, >= 70 => 2, _ => 0 };
```

Today `Tidy.Grade(int)` is Unknown(opaque), reason `switch-pattern`. `IOPERATION-COVERAGE.md` row
`IsPattern` lowers a constant pattern, a discard and a type test, and leaves every other pattern
opaque. A relational pattern on an integral scrutinee is the comparison it spells, and has no
exception edge. Lower it, so that the pair is Equivalent.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` rows `IsPattern`, `SwitchExpression` and `Binary`;
`samples/cleanup-modern-syntax/README.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A `RelationalPattern` inside `IsPattern`, whose scrutinee and constant have the same bitvector
   type, lowers to the signed or unsigned comparison the `Binary` row uses for that operator and
   type. Any other relational pattern stays opaque with reason `switch-pattern`.
2. `IOPERATION-COVERAGE.md` has a `RelationalPattern` row, and the `IsPattern` row names it.
3. `Tidy.Grade(int)` in `samples/cleanup-modern-syntax` is Equivalent. The sample's snapshot and
   README row are updated.

## Files
`src/Equiv.Frontend.CSharp/**` (the lowering of `IsPattern`), its test project,
`docs/tickets/IOPERATION-COVERAGE.md`, `samples/cleanup-modern-syntax/expected.sarif.json` and
`README.md`.

## Tests
- A lowering unit test and a snapshot test for a `switch` expression with relational patterns.
- `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp` covers the new form.
- `SamplesEndToEndTests` row for `cleanup-modern-syntax`.

## Size guard
A new IR node means the comparison is not being reused: stop.

## Out of scope
`and`, `or` and `not` pattern combinators, list patterns, property patterns, and relational patterns
on floating-point, `decimal` or `char` scrutinees.

## Notes
- Found by P2-049 (`samples/cleanup-modern-syntax`, `Tidy.Grade`).
