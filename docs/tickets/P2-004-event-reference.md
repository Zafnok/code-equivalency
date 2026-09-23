# P2-004 Reading and raising a field-like event is lowered, not opaque
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-010, P1-005

## Goal
M3-022's census found `EventReference` in two agent pairs' top fifteen (4 bodies), and no ticket
owned it. Minimal repro:

```csharp
sealed class Counter
{
    public event EventHandler? Changed;
    int n;
    public void Bump() { n++; Changed?.Invoke(this, EventArgs.Empty); }
}
```

Inside its declaring type, a field-like event is a delegate field. After this ticket, an
`IEventReferenceOperation` read inside the declaring type lowers to a read of the backing field's
`field.<Type>.<Event>` map, exactly as a field read. `?.Invoke` then goes through the existing null
shadow and call lowering.

## Spec references
VERIFICATION-MODEL section 2 (field maps); ADR 0018; `docs/tickets/IOPERATION-COVERAGE.md` row
`EventReference`; the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. `Bump` lowers with no `IrOpaque`, snapshot-tested.
2. An event with explicit `add`/`remove` accessors, or one read from outside its declaring type,
   stays `IrOpaque("EventReference")`.
3. The coverage-table row names the tests.

## Size guard
Reads only. `+=` and `-=` are P2-005.

## Out of scope
Custom event accessors; `EventAssignment` (P2-005).

## Notes
