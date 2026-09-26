# P2-008 `?.` and `??` null tests are lowered through the null shadow
Status: done (PR #214)
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-010

## Goal
M3-022's census found `IsNull` in SignalR.Extras.Autofac's top fifteen (4 of 25 bodies) and in
ServiceAnt, and no ticket owned it. Roslyn's CFG turns `?.` and `??` into an
`IIsNullOperation` on a flow capture, which falls to the lowerer's default arm. Minimal repro:

```csharp
static int Len(string? s) => s?.Length ?? 0;
static string Name(Person? p) => p?.Name ?? "anonymous";
```

After this ticket, `IIsNullOperation` lowers to a read of the operand's null shadow (`null.<T>`),
the same value `x == null` already reads. A non-nullable value type operand is constant false.

## Spec references
VERIFICATION-MODEL section 2 (null shadow); `IrLowerer.Binary` (`x == null`);
`docs/tickets/IOPERATION-COVERAGE.md` row `IsNull`; the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. Both repro methods lower with no `IsNull` opaque, snapshot-tested. The second has no `IrOpaque` at all;
   the first keeps its `Nullable<T>` `Conversion` and `DefaultValue` opaques (see Deviation).
2. `s?.Length ?? 0` branches first on `s`'s null shadow, as `s == null ? 0 : s.Length` does, and its `??`
   branches on the null shadow of the `int?` capture, not on an opaque, unit-tested.
3. The coverage-table row names the tests.

## Size guard
One lowering arm. `Nullable<T>`'s `HasValue` is a property read (M3-010), not this ticket.

## Out of scope
`??=` (P2-006).

## Notes
- M4-001 added the reference-typed arm (a `using`/`foreach` `finally` needed it): `IIsNullOperation`
  on a reference-typed operand already reads its nullness, so the two repro methods may already have
  no `IsNull` opaque. What is left here is the snapshots, the branch-structure test, the coverage
  row's tests, and a non-reference operand (`Nullable<T>`), which is still opaque `IsNull`.
- Deviation: criteria 1 and 2 as first written cannot hold without modelling `Nullable<T>`, which the Size guard
  rules out. Roslyn's CFG lowers `s?.Length` to an `int?` capture (the conversion `int` to `int?` and `default(int?)`,
  opaque `Conversion` and `DefaultValue`) and `?? 0` to an `IsNull` of that capture and `GetValueOrDefault()`. So
  repro 1 keeps those two opaques after this ticket (repro 2, all reference-typed, has none), and it has a second
  branch that `s == null ? 0 : s.Length` lacks. Criterion 2 is reworded to what holds; the lifted conversion and
  `default(T?)` belong to a `Nullable<T>` ticket.
- Decision: an `IsNull` of a `Nullable<T>` operand reads `null.System.Nullable_1` like a reference's (the one arm the
  Goal names); an unconstrained type parameter's stays opaque `IsNull`. Rule: smallest change that meets the Goal.
- Decision: no constant-false arm for a non-nullable value type. Roslyn never emits `IsNull` on one (a `using` of a
  `struct`-constrained `T` has no null test; `?.` and `??` on a non-nullable struct do not compile), so the arm would
  be unreachable and fail the coverage gate.
- Decision: a null test of a flow capture reads the capture's own `isNull` shadow, set when it was captured, instead of
  the lvalue the capture may stand for or a fresh `null.<T>` read. Without it, `p?.Name ?? "anonymous"` tests the
  constant null through `null.System.String` and does not know it is null. The `foreach` snapshots change the same way
  (the enumerator's shadow instead of a repeated map read of the same value).
