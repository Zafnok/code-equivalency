# P2-006 Assignment through a flow capture with no registered target is lowered
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-010

## Goal
M3-022's census found `FlowCaptureReference` in adapters-shortest-paths-dotnet's top fifteen (4
bodies), and no ticket owned it. The reason comes from `IrLowerer.Assign` (and the compound-assignment
path) when the target is an `IFlowCaptureReferenceOperation` that is not in `captureTargets`, so the
capture holds something other than a local or parameter. The census cannot say which construct
does that. The likely shapes, to confirm first with a failing unit test each:

```csharp
sealed class Cache
{
    List<int>? items;
    public List<int> Items() => items ??= new List<int>();   // ??= on a field
    public void Reset(Holder? h) { if (h != null) h.Value ??= 0; }  // ??= on a property
}
```

After this ticket, a flow capture that holds a field or property reference is a valid assignment
target: the write goes to that field map or setter call.

## Spec references
`IrLowerer.Target`, `Assign`; M3-010 (property writes); `docs/tickets/IOPERATION-COVERAGE.md` rows
`FlowCapture` and `FlowCaptureReference`; the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test pins down each construct that produced `FlowCaptureReference`, and it is listed in
   Notes.
2. Each confirmed construct lowers with no `IrOpaque`, snapshot-tested.
3. The coverage-table row names the tests.

## Size guard
If a construct needs more than the field or setter write path, keep it opaque and list it in Notes.

## Out of scope
Captures of array elements (P1-006).

## Notes
Constructs confirmed to produce `FlowCaptureReference` (each failed with that reason before the fix and
now lowers with no `IrOpaque`):
- `??=` on a field, instance or static (`items ??= new List<int>()`, `s ??= t`):
  `IrLowererTests.ANullCoalescingAssignmentToAFieldWritesItOnlyWhenItWasNull`,
  snapshot `NullCoalescingAssignmentToAField`.
- A field assigned a value that branches (`h.F = b ? 1 : a`, `f = b ? 1 : a`):
  `IrLowererTests.AFieldAssignedABranchingValueIsWrittenThroughTheCapture`,
  `AFieldOfANullReceiverAssignedABranchingValueThrows`, snapshot `FieldAssignedABranchingValue`.
- A compound assignment of a branching value to a field (`f += b ? 1 : a`):
  `AFieldAssignedABranchingValueIsWrittenThroughTheCapture`, snapshot
  `CompoundAssignmentToAFieldOfABranchingValue`.

The ticket's second guess, `??=` on a property, did not produce `FlowCaptureReference`: M3-010 already
wrote it through the setter, but its read loaded a capture that was never stored, so it was opaque with
reason `undefined`. The same fix reads such a capture through the getter (or the auto-property's map):
`ANullCoalescingAssignmentToAPropertyCallsTheGetterThenTheSetter`, snapshot
`NullCoalescingAssignmentToAProperty`. A captured array element stays opaque with reason
`FlowCaptureReference` (out of scope, P1-006), pinned in `UnsupportedConstructIsOpaqueWithItsName`.

Decision: a captured field joins the existing auto-property `sliceTargets` path rather than a new map, and a
capture that stands for a place has no null shadow (its nullness is the null map's), per `equiv-decide`
(reuse the nearest existing mechanism).

Found in passing, not this ticket's: `??=` on a `Nullable<T>` (`static int M(int? f) => f ??= 3;`, a parameter
too, so not a flow-capture target issue) produces IR that fails validation, a crash rather than an opaque;
`??=` on an `int?` property is opaque with reason `Conversion`.
