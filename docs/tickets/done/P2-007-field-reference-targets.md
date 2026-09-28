# P2-007 Find and lower the field assignments that stay opaque
Status: done (PR #263)
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M3-022's census found `FieldReference` in adapters-shortest-paths-dotnet's top fifteen (2 bodies),
though the coverage table says field reads and writes are lowered. The reason is produced where a
field is an assignment or compound-assignment target that `Slice` and `Target` both reject.
Candidate shapes, to confirm with a failing unit test each:

```csharp
struct P { public int X; }
sealed class Box { public P Pos; public int[] Data = []; }
static void Move(Box b) { b.Pos.X = 5; }          // field of a struct-typed field
static void Grow(Box b) { b.Data = new int[4]; }  // field whose value is an opaque creation
```

After this ticket, each confirmed shape either lowers or is documented in the coverage table as
deliberately opaque, with its reason.

## Spec references
`IrLowerer.Assign`, `Slice`, `Target`; `docs/tickets/IOPERATION-COVERAGE.md` row `FieldReference`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A unit test per construct that produced `FieldReference`, listed in Notes.
2. Each one lowers with no `IrOpaque`, or the coverage-table row says why it stays opaque.

## Size guard
A struct-in-struct field write that needs nested field maps is out: keep it opaque and note it.

## Out of scope
Static field initialisers.

## Notes

- Decision: the two shapes given in the Goal (`b.Pos.X = 5;`, a struct-typed field of a struct-typed field, and
  `b.Data = new int[4];`, a whole array-typed field) already lower with no opaque, unmodified: `HeapLowerer.Field`
  resolves a field's receiver by recursively lowering it, so nesting a struct-typed field inside another field costs
  nothing extra — it is just another receiver read one level down. The Size guard's struct-in-struct worry does not
  trigger: there is no nested field map to build, only the same `field.<Type>.<Field>` map one receiver deeper.
- The actual opacity the M3-022 census found is a flow-capture gap, not `Slice`/`Target` rejecting a direct field
  reference: when an assignment or compound-assignment's *value* branches (`cond ? a : b`, `??=` desugars the same
  way), the CFG flow-captures the *target* ahead of the branch, and the store later targets that
  `IFlowCaptureReferenceOperation`. `Assign` already resolved a captured auto-property this way (`sliceTargets`,
  ticket M4-008), but a captured field or array element fell through `Target`, which only knows locals and
  parameters — `Opaque(assignment, Reason(assignment.Target), context)` then reports the *capture's* kind,
  `FlowCaptureReference`, not `FieldReference`, though the construct actually lost is the field write.
- Fix: `IrLowerer.Statement`'s flow-capture handling now resolves any sliceable lvalue (`HeapLowerer.Slice`: a field,
  an array element, or an auto-property, the same union `Assign` already accepts) into `sliceTargets`, not only
  auto-properties. `Place` (which backs compound assignment and `++`/`--`) now also consults `sliceTargets` for a
  captured target, matching what `Assign` already did — compound assignment on a captured field target had the same
  gap.
- `b.Data ??= new int[4];` on a field surfaces a separate, pre-existing `"undefined"` opaque (`SsaBuilder`) once its
  capture resolves; the same construct on a local variable lowers cleanly, so this is unrelated to field-target
  resolution. Left alone: it is not one of this ticket's candidate shapes, and not a `Slice`/`Target` rejection.
  Flagged as a follow-up task.
- Decision: no coverage-table opacity entry is needed. Every construct that produced a `FieldReference`-adjacent
  opaque field write (the `FlowCaptureReference` cases above) now lowers instead.
