# P2-030 `sizeof(T)` (`SizeOf`) has no lowering
Status: in-progress
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M4-007's first real run found `SizeOf` in Git Extensions' opaque reasons (1 occurrence), and no
ticket or `IOPERATION-COVERAGE.md` row owns it. Minimal repro:

```csharp
unsafe static int SizeOfInt() => sizeof(int);
```

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (no `SizeOf` row yet); `TypeOf`'s row (`sizeof` is the
numeric analogue of `typeof` for an unmanaged type, and Roslyn folds `sizeof` of a built-in type to
a compile-time constant the same way it folds other constants, per `Conversion`'s row); the
`equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, via `equiv-decide`: `sizeof` of a built-in/blittable type is a Roslyn constant already
   (same path as any other folded constant) — confirm whether the frontend already takes that path
   and this opaque only fires for a user-defined unmanaged struct's `sizeof`, or whether it never
   takes the constant path yet. Lower the constant case if not already handled; leave a
   user-defined struct's `sizeof` opaque with reason `SizeOf` and add the `IOPERATION-COVERAGE.md`
   row saying so.
2. A test confirming the repro's `sizeof(int)` folds to the constant `4`.

## Size guard
Built-in types only. A user-defined struct's `sizeof` (layout-dependent) stays opaque; do not add
struct layout modelling here.

## Out of scope
`Marshal.SizeOf`, struct layout in general.

## Notes
- Decision: no lowering change. `IrLowerer` lowers any operation whose `ConstantValue.HasValue` as a constant before dispatching on its kind, and Roslyn folds `sizeof` of a built-in type (`sizeof(int)` = 4, also allowed outside `unsafe`), so the constant path already took it. The Git Extensions `SizeOf` opaque is therefore a user-defined struct's `sizeof`, which has no constant and falls to the default `operation.Kind` opaque; it stays opaque, per the Size guard.
- The struct test uses `ErroneousBody` because the test compilation does not allow unsafe code (CS0233 outside `unsafe`); the bound tree is still an `ISizeOfOperation` with no constant.
