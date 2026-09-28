# P2-023 `&x` (`AddressOf`) has no lowering
Status: in-progress
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
- Decision: `&x` -> stays opaque with reason `AddressOf` (the default arm already produces it; no lowering change). Alternatives: lower as a `ref` alias of `x`, a new pointer sort. Rule: 4 (a pointer value is observable only through indirection or arithmetic, which is out of scope and would need pointer IR, the Size guard's ADR trigger).
- Deviation: the Goal's repro `unsafe static int* Address(ref int x) => &x;` does not compile (CS0212: a `ref` parameter is a moveable variable, so its address needs a `fixed` statement). The snapshot uses the nearest valid form, a by-value parameter: `unsafe static int* M(int x) => &x;`.
- Decision: test compilations (`RoslynTestCompilations.Compile`) allow unsafe code, so a snippet can hold `&x`; allowing it only widens what compiles. Alternatives: an `allowUnsafe` parameter threaded through `Lowered`. Rule: 4.
- The snapshot pins "not shared": `IrText` prints ` fragment "<hash>"` on a fingerprinted opaque and the dump has none. Roslyn's `AnalyzeDataFlow` counts the operand of `&` as written inside, so `FragmentFingerprinter` refuses it as a write of an outer variable.
