# P2-006 Assignment through a flow capture with no registered target is lowered
Status: todo
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
