# P2-004 Reading and raising a field-like event is lowered, not opaque
Status: in-progress
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
- Decision: Roslyn's public API does not list a field-like event's backing field in `GetMembers()`, so the map is keyed off
  the event symbol (`HeapInputs.Field(IEventSymbol)`); it is named for the event, the same name the compiler gives the
  backing field, so a field initializer (`event Action E = null;`) writes the same `field.C.E` slice.
- Decision: `Bump`'s `n++` was itself opaque (`FieldReference`): a field was never a compound or increment target. AC1
  needs it, so `IrLowerer.Place` now takes a field's slice as it took an auto-property's. Array elements stay opaque as
  compound targets. `IrLowererTests.CompoundAssignmentToAnUnsupportedTargetIsOpaque` lost its `f += a` case; the new
  `AFieldIsACompoundTarget` covers it. This may be one of P2-007's candidate shapes.
- Deviation: AC2's explicit-accessor half holds vacuously. C# rejects reading an event with explicit accessors (CS0079;
  an erroneous body binds it as `Invalid`), so its only `EventReference` is under `+=`/`-=`, which stays
  `IrOpaque("EventAssignment")` (P2-005). A syntax check for it would be an unreachable branch under the 100% gate, so
  there is none; `AnEventWithExplicitAccessorsIsOpaque` pins the `EventAssignment` opaque instead.
