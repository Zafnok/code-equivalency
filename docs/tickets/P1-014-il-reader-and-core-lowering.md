# P1-014 IL fallback, part 1: read a method's ILAst and lower its control flow, integral arithmetic and calls
Status: in-progress
Effort: L
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-012, M4-009; ADR 0039 accepted

## Goal
The first half of ADR 0039's lowering, not yet wired into any run. `ICSharpCode.Decompiler` becomes a
shipped dependency of `Equiv.Frontend.CSharp`. A new `IlLowerer` takes one method symbol and its
loaded `Compilation`, emits the compilation in memory with a portable PDB, reads the method as
ILAst through P1-012's pipeline, and lowers the control-flow, integral and call subset of P1-012's
mapping table to the same IR `IrLowerer` produces. Every other instruction is an `IrOpaque` whose
reason is its ILAst key. About 6 source files and 3 test files.

## Spec references
ADR 0039 (all of it); VERIFICATION-MODEL.md section 3.1, section 2 (`IrCall`, `IrOpaque`); ADR 0018
(calls and the heap); ADR 0021 (parameters by position); ADR 0002 and ADR 0017 (a new package);
`docs/runs/2026-09-28-il-lowering-spike.md` (the mapping table and the transform list).

## Design
- **Reader (`Lowering/Il/IlAstReader.cs`).** Port the spike's reader: `Emit` to a `MemoryStream`
  with `DebugInformationFormat.PortablePdb` to a second stream, one `PEFile` and `DecompilerTypeSystem`
  per compilation (cached per `Compilation` for the run), `IdStringProvider.FindEntity` on the
  symbol's documentation ID, `ILReader` with the PDB, and the spike's pipeline without the
  "relifting" transforms. The removed list is a checked-in constant, and a test pins it, so a
  Decompiler upgrade that adds a relifting transform fails loudly. A failed emit or lookup returns
  a reason (`il-emit-failed`, `il-method-not-found`, `il-no-body`), never throws.
- **Symbols (`Lowering/Il/IlSymbols.cs`).** ILSpy `IType` and `IMember` → Roslyn `ITypeSymbol` and
  `ISymbol` of the loaded compilation, through `IdStringProvider.GetIdString` and
  `DocumentationCommentId.GetFirstSymbolForReferenceId`. Generic instantiations are rebuilt with
  `Construct` from resolved type arguments. Then the existing `TypeMapper` and `CallIdentityFactory`
  run unchanged. An unresolvable reference is an opaque with reason `il-unresolved`.
- **Lowering (`Lowering/Il/IlLowerer.cs`).** It walks the `BlockContainer` into `IrBlock`s and uses
  `SsaBuilder` as `IrLowerer` does, with parameters named as ADR 0021 names them. The mapped keys in
  this ticket are:
  - `ILFunction`, `BlockContainer`, `Block`, `Nop`, `Branch`, `Leave`, `IfInstruction`,
    `SwitchInstruction`, `SwitchSection`;
  - `LdLoc`, `StLoc`, `LdcI4`, `LdcI8`, `LdStr`, `LdNull`;
  - `BinaryNumericInstruction`, `Comp` and `LogicNot` on integral and `bool` operands, with the same
    overflow and divide edges `IrLowerer` emits, and `Conv` between integral types;
  - `Call`, `CallVirt` and `NewObj` as `IrCall` with its `threw` edge, the receiver's null check and
    the heap pairs `HeapInputs` lists.

  Everything else is an `IrOpaque` with its key as reason (`MappingTable.Key`'s spelling), its
  source span from the nearest sequence point, and a fingerprint of its ILAst text.
- **Pitfalls.** ILAst's `Sign` on a `BinaryNumericInstruction` decides `sdiv`/`udiv` and the
  comparisons, not the operand types. `Leave` from the function's container is a return. A `Comp`
  on references is a reference equality, which this ticket leaves opaque (P1-015 reads the null
  shadow). The IR a method lowers to must pass `IrValidator`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `ICSharpCode.Decompiler` is in `Directory.Packages.props`, referenced by `Equiv.Frontend.CSharp`
   only, and has an ADR 0002 product row that replaces the spike row. `tools/licence-check` passes
   and `THIRD-PARTY-NOTICES.md` is regenerated.
2. An architecture test fails if any project other than `Equiv.Frontend.CSharp` references the
   `ICSharpCode.Decompiler` namespace.
3. `IlLowerer.Lower(IMethodSymbol, Compilation)` returns an `IrProcedure` that passes `IrValidator`
   for every method in `samples/` that has a body. Every non-mapped instruction is an `IrOpaque`
   whose reason is its ILAst key.
4. For every method in `samples/` whose IOperation lowering holds no `IrOpaque` and whose ILAst uses
   only this ticket's keys, both lowerings name the same callee identities, in the same order, and
   the same parameter names and sorts.
5. An IL oracle property test runs generated straight-line and branching integral methods, with
   calls to a fixed helper, through the CLR and through `IrInterpreter` on the IL lowering, and the
   outputs agree.
6. `docs/tickets/IL-COVERAGE.md` exists with one row per ILAst key: `lowered` (with its tests and
   ticket) or `opaque` (with the reason), in `IOPERATION-COVERAGE.md`'s format.
7. No run uses the IL lowering yet: `CSharpFrontend.Analyze` is unchanged.

## Files
- `Directory.Packages.props`, `src/Equiv.Frontend.CSharp/Equiv.Frontend.CSharp.csproj`, both lock files it changes
- `src/Equiv.Frontend.CSharp/Lowering/Il/IlAstReader.cs`, `IlSymbols.cs`, `IlLowerer.cs`, `IlKeys.cs`
- `tests/Equiv.Frontend.CSharp.Tests/Lowering/Il/IlLowererTests.cs`, `IlLoweringOracleTests.cs`, `IlAstReaderTests.cs`
- `tests/Equiv.Tests.Architecture/DependencyRuleTests.cs`
- `docs/adr/0002-dependencies.md`, `THIRD-PARTY-NOTICES.md`, `docs/tickets/IL-COVERAGE.md`

## Tests
- `IlAstReaderTests.EveryReliftingTransformIsRemoved`, `.AMissingMethodIsAReasonNotAnException`
- `IlLowererTests.EverySampleMethodLowersToValidIr`, `.UnmappedInstructionsAreOpaqueWithTheirKey`,
  `.CallIdentitiesMatchTheOperationLowering`, `.UnsignedDivisionFollowsTheInstructionSign`
- `IlLoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`
- `DependencyRuleTests.OnlyTheCSharpFrontendReferencesTheDecompiler`

## Size guard
More than 7 source files, or any change to `IrLowerer.cs` beyond making a helper `internal`, means
you are doing P1-015 or P1-016.

## Out of scope
Fields, arrays, addresses, casts, boxing, exceptions, floating point and `decimal` (P1-015). Choosing
the fallback per pair, `--il-fallback`, SARIF and the census (P1-016). Deleting `tools/spikes/il-lowering`.

## Notes
