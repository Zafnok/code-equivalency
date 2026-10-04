# P2-126 A string literal is never null, so a call on one makes no null check
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-116

## Goal
The IOperation lowering null-checks the receiver of an instance call. When the receiver is a string
literal it reads the literal's nullness from `null.System.String`, which is free, so the lowered
method has a path that throws `NullReferenceException` before the call. A literal is never null. The
IL lowering makes no null check of a `ldstr`. Minimal repro (`samples/lone-surrogate-constant`):

```csharp
static int Find(string s) => s.IndexOfAny("\uD800\uDBFF".ToCharArray());
```

`IlLoweringParityTests` verifies the two lowerings of this method as Divergent, the IOperation side
throwing `NullReferenceException`. Both sides of a migration pair get the same false path, so it costs
no verdict when the two sides are the same, but a pair where one side calls a member on a literal and
the other on a value known not to be null has a false counterexample.

Lower a non-null constant of a reference type as not null: no null check on it as a receiver, and a
null test of it is false.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (`Literal`, `Invocation`), M3-010 (null shadows), ADR 0039.

## Acceptance criteria (all must hold; nothing beyond them)
1. The IOperation lowering of the repro has no `NullReferenceException` path for the literal receiver (unit test).
2. The two `lone-surrogate-constant` entries leave `IlLoweringParityTests.Known` and
   `IlLowererTests.KnownCalleeDifferences`, and both tests pass.
3. No `samples/*/expected.sarif.json` verdict gets worse.

## Tests
The unit test in criterion 1; the two parity tests.

## Out of scope
Nullness of anything that is not a constant (fields, method results).

## Notes
- Found by P2-116: its sample is the first with a call on a string literal.
