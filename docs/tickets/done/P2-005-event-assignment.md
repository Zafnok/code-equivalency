# P2-005 Event `+=` and `-=` are calls to the accessors, not opaque
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-010, P1-005

## Goal
M3-022's census found `EventAssignment` in SignalR.Extras.Autofac's reasons, and no ticket owned
it. Minimal repro:

```csharp
static void Wire(Button b, EventHandler h) { b.Clicked += h; }
static void Unwire(Button b, EventHandler h) { b.Clicked -= h; }
```

After this ticket, `IEventAssignmentOperation` lowers to an `IrCall` of the event's `AddMethod` or
`RemoveMethod` with the receiver and the handler, exactly as M3-010 lowers a property write to its
setter: receiver null check, call identity, `threw` edge.

## Spec references
M3-010 acceptance criterion 2 (setter calls); ADR 0018; `docs/tickets/IOPERATION-COVERAGE.md` row
`EventAssignment`; the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. Both repro methods lower to one accessor call each, snapshot-tested.
2. A handler that is a lambda keeps that lambda's own reason (`DelegateCreation`, M4-004).
3. The coverage-table row names the tests.

## Size guard
One lowering arm reusing M3-010's accessor-call path. If that path needs changing, stop.

## Out of scope
Event reads and raising (P2-004).

## Notes
- Decision: the arm calls `Dispatch` (M3-010's accessor-call path under `Accessor`) directly rather than `Accessor`, which
  takes an `IPropertyReferenceOperation`; the path itself is unchanged, so the size guard holds.
- Decision: an event with explicit accessors lowers the same way. The compiler calls `add_E`/`remove_E` for every event,
  field-like ones inside their own type included, so there is no reason to keep it opaque. This retires P2-004's
  `AnEventWithExplicitAccessorsIsOpaque`; its case is now a row of `AnEventAssignmentCallsItsAccessor`.
- Decision: the property-read and event-assignment cases share one `switch` arm (`AccessorCall`) because a separate arm
  pushed `IrLowerer.Operation` past MA0051's 60 lines.
- Note: `IEventAssignmentOperation.EventReference` is typed `IOperation`; it is cast, not matched, because only erroneous
  code makes it anything else, and a match would be an unreachable branch under the 100% gate.
