# P2-008 `?.` and `??` null tests are lowered through the null shadow
Status: todo
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
1. Both repro methods lower with no `IsNull` opaque, snapshot-tested. After M3-010 they have no
   `IrOpaque` at all.
2. `s?.Length ?? 0` and `s == null ? 0 : s.Length` lower to the same branch structure, unit-tested.
3. The coverage-table row names the tests.

## Size guard
One lowering arm. `Nullable<T>`'s `HasValue` is a property read (M3-010), not this ticket.

## Out of scope
`??=` (P2-006).

## Notes
