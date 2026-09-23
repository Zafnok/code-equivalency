# P2-005 Event `+=` and `-=` are calls to the accessors, not opaque
Status: todo
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
