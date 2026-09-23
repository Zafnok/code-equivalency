# P2-007 Find and lower the field assignments that stay opaque
Status: todo
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
