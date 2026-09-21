# M3-007 Field and array maps are `Ref` parameters, so the final heap is observable
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-004

## Goal
`void Set(int v) { this.x = v; }` stops being Equivalent to `void Set(int v) { }`. The
synthesised `field.*` and `array.*` map parameters become `IrParameterKind.Ref`, so every exit's
`outs` names each map's final SSA version. The final heap then becomes an observable through the
by-ref comparison M3-001 already performs. That is the frontend half of ADR 0018's "heap as
output". The encoder half (a `Ref` parameter present on only one side is compared against the
shared input) is M3-001 criterion 10.

## Spec references
VERIFICATION-MODEL.md sections 1 and 2 (the synthesised-inputs paragraph); ADR 0018; M1-002's
`outs` rule (`IrReturn`/`IrThrow` list every by-ref parameter once, in declaration order).

## Acceptance criteria (all must hold; nothing beyond them)
1. `HeapInputs.Parameters` returns `field.*` and `array.*` inputs as `IrParameterKind.Ref`, and
   `this`, `null.*` and `length.*` as `In`. Ordering is unchanged (by name). `HeapInputs`'s XML
   doc says which kind each input is, and why.
2. Every `IrReturn` and `IrThrow` the lowerer emits lists, for each `Ref` heap map, the SSA
   version live at that exit. On a path that never writes the map, that version is the map's
   input. `IrValidator` reports zero diagnostics on every lowered sample and on every snapshot
   fixture (it already enforces the `outs` rule).
3. Heap maps are created lazily, when first touched, so an exit lowered before a map existed
   still gets the map's out. Exits are completed after the whole body is lowered. Test
   `ExitsLoweredBeforeAFieldIsTouchedStillNameItsFinalVersion`: an early `return` precedes the
   first field write in source order, and that `return`'s outs name the field map's input.
4. `IrInterpreter` results for `Ref` heap maps appear in the run's outs, just as C# `ref`
   parameters' do. The lowering oracle compares them when the generated method writes a
   static field, so `LoweringOracleGen` gains a case that writes a static `int` field and
   returns nothing.
5. Every `.verified.txt` snapshot that touches a field or array is re-approved. The diff in each
   is only the parameter kind and the added outs.
6. VERIFICATION-MODEL section 2 already states the target model (ADR 0018). Check that it
   matches what was built; if it does not, correct the code, not the spec.

## Files
`src/Equiv.Frontend.CSharp/Lowering/HeapInputs.cs`, `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`
(exit completion), `tests/Equiv.Frontend.CSharp.Tests/Lowering/*` (new test, re-approved
snapshots), `tests/Equiv.TestSupport/LoweringOracleGen.cs`.

## Tests
`ExitsLoweredBeforeAFieldIsTouchedStillNameItsFinalVersion`, `HeapMapsAreRefAndNullAndLengthAreIn`,
the extended lowering oracle, and the re-approved snapshots.

## Size guard
No new `src/` file. If `Equiv.Core` needs a change, stop: `Ref` parameters and `outs` already
exist.

## Out of scope
The encoder (M3-001). Heap effects of calls (P1-005). Array keying (P1-006). Treating
`null.*` as writable.

## Notes
