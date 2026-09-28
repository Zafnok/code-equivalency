# P2-023 `&x` (`AddressOf`) has no lowering
Status: todo
Effort: S
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
M4-007's first real run found `AddressOf` in Git Extensions' opaque reasons (1 occurrence,
`EasyHook.LocalHook` P/Invoke marshalling code), and no ticket or `IOPERATION-COVERAGE.md` row
owns it. Minimal repro:

```csharp
unsafe static int* Address(ref int x) => &x;
```

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` (no `AddressOf` row yet); the `equiv-extend-ir` skill.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, via `equiv-decide`, whether `&x` on a fixed/local/parameter lowers to something
   observable (e.g. treated like a `ref` alias) or stays opaque with reason `AddressOf`; either
   way, add the `IOPERATION-COVERAGE.md` row.
2. A snapshot test for the repro's lowering (or its opaque fallback) with a fingerprint check if
   shared.

## Size guard
One `IOperation` kind. If handling it requires new IR instructions for pointer arithmetic, stop
and write an ADR instead.

## Out of scope
Pointer arithmetic, `fixed` statements beyond what the repro needs.

## Notes
