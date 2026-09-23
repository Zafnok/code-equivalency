# P2-009 A local written only by an opaque has a defined value
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M3-022's census found `undefined` in ServiceAnt and SignalR.Extras.Autofac, and no ticket owned it.
`SsaBuilder.Undefined` emits it when a variable is read with no reaching definition. C#'s definite
assignment rules out that case in source, so something the lowerer skips must have been the write.
In both pairs it appears with exactly one `ref-argument` opaque, which points at an `out` argument
of an opaque call. Candidate repro, to confirm with a failing unit test first:

```csharp
static int ParseOr(string s, int fallback) => int.TryParse(s, out var n) ? n : fallback;
```

After this ticket, an opaque that stands for a construct writing a local, parameter or capture
(`out`/`ref` argument, deconstruction, pattern binding) also writes a fresh opaque value to each
written variable. A later read then sees that value, not `undefined`. Until M4-003 lowers `ref`/`out`
for real, this is what keeps such methods well-formed.

## Spec references
`SsaBuilder.Undefined`; `IrLowerer.Opaque`; ADR 0014 (opaque semantics).

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test pins down the construct that produced `undefined`, listed in Notes.
2. That construct's lowering has one `IrOpaque` per written variable with the construct's own
   reason, and no `undefined`, snapshot-tested.
3. No existing snapshot gains an `undefined`.

## Size guard
If the fix needs changes in `SsaBuilder` beyond defining the written variables, stop.

## Out of scope
Lowering `ref`/`out` for real (M4-003).

## Notes
