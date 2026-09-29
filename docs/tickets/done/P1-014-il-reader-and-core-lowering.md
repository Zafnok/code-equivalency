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
- Decision: the sample methods of criteria 3 and 4 are compiled in-process from each sample side's sources against the test run's framework (`IlSamples.cs`), with stub types for `webapi-basic`'s ASP.NET attributes and results, instead of loading the solutions through MSBuild. Alternatives: the tests in `Equiv.Tests.Integration` with `MsBuildSolutionLoader`. Rule: the ticket puts them in `Equiv.Frontend.CSharp.Tests`, which runs on Linux and loads nothing.
- Decision: criterion 4's "same order" compares each path's callee sequence (entry to exit, a path cut where it would re-enter a block), as a set. Alternatives: the calls in block order. Rule: ILSpy negates a condition and swaps its arms (`Find` in `webapi-basic`), so two lowerings of one method lay out the same paths in different block orders; per-path order is the order a run makes the calls.
- Decision: a null shadow for every parameter, source-declared local and stack slot of a reference type, none for a local the compiler adds (Debug's return temporary). Alternatives: every variable; none (P1-015). Rule: criterion 4's heap inputs: the IOperation lowering shadows parameters, locals and flow captures (a stack slot here), and has no variable for a compiler temporary, so shadowing it added a `null.<Sort>` input the IOperation lowering lacks.
- Decision: an auto-property's accessor call is its backing field's map (M4-008) and a user-defined operator or conversion its `op:` pure function (M4-002), through `HeapLowerer` and `PureCatalogue`, as `IrLowerer` lowers them. Alternatives: an `IrCall` for every `Call`. Rule: criterion 4 and ADR 0039's "the same identity and sort code": `business-layer`'s `CustomerName` is a field read in IOperation lowering, so a call to `get_CustomerName` named a callee the other lowering does not.
- Decision: a local starts at its type's default, stored first in the entry. Alternatives: an `undefined` opaque where no store reaches. Rule: the oracle found Roslyn's optimiser folding `f = false; f ? k : n` into `if (0 == 0)`, whose dead arm reads a split `k` no store reaches; IL's `.locals init` gives it the default, and C#'s definite assignment leaves only infeasible paths reading a local before a store.
- Decision: a mapped instruction the IR has no types for (a comparison of references, a call with a `ref` argument, a local of a closure class) lowers its operands first, each unmapped one an opaque of its own key, then is an opaque of its own key, never fingerprinted; an unmapped one is one opaque over its whole subtree, fingerprinted when it qualifies. Alternatives: one opaque over the whole subtree either way. Rule: criterion 3's "every non-mapped instruction is an `IrOpaque` whose reason is its ILAst key" (`UnmappedInstructionsAreOpaqueWithTheirKey`).
- Decision: an unmapped statement that cannot complete (a `throw`, a `try` every path of which leaves) ends its block with a return of the opaque's value, as `IrLowerer`'s `OpaqueExit` does. Rule: IR validity; any input reaching the opaque is Unknown(Opaque) anyway.
- Decision: only `callvirt` null-checks its receiver. Rule: IL's own semantics; Roslyn emits `call` only for receivers it knows are not null (`this`, `base`, a `new`), which `Nullness` already treats as never null.
- Decision: a value of one sort used as another reads `cast.<From>.<To>` without asking Roslyn for the conversion. Rule: IL that verifies changes a reference's type with no instruction only by an implicit reference conversion, and the check left a branch no C# reaches.
- Decision: a `switch` is always one `IrSwitch`, every label of every section but the default enumerated. Alternatives: an interval chain for wide sections. Rule: C# gives no non-default section a wide range (checked with relational patterns and contiguous cases); the default takes the rest.
- Decision: the fingerprint's text is ILAst's own `WriteTo`, through an `ITextOutput` of ours that spells each member as its declaring type and documentation ID, each type as its reflection name and each local by first appearance. Alternatives: ILAst's plain text. Rule: soundness: the plain text names a callee by its short name only (`call Max`), so two different callees would share a fingerprint.
- Decision: `LogicNot` is not a key: ILSpy 11 has no such instruction (`!b` is a `Comp` against 0); IL-COVERAGE has an `n/a` row for it. `LdVirtFtn[lambda]` is dropped from the spike's refinements: a lambda is never virtual.
- Decision: a syntax tree with no encoding (one parsed from a string, as every test compilation is) is re-parsed with UTF-8 before the emit, since a portable PDB records each document's checksum (CS8055); a loaded project's trees keep theirs, so its bound state is reused. PDB documents are named back to their tree's path through the compilation's `SourceReferenceResolver` (`PathMap`), as an IOperation span is.
- Deviation: `HeapLowerer.Inlined` is made `internal` too (the Size guard names only `IrLowerer.cs`, whose `Signature` and signature-level `Opaque` are made `internal`); the IL lowering's auto-property rule needs the same predicate.
- Deviation: `.editorconfig` turns off MA0182 for `Lowering/Il/IlLowerer.cs` until P1-016 wires its caller (criterion 7 forbids one now), and CA2000 for `Lowering/Il/IlAstReader.cs`, whose cached image a `ConditionalWeakTable` never disposes; both sections say why.
- Deviation: files beyond the list: `IlFragment.cs` (the fingerprint), `IlSamples.cs` (the sample compilations), `tools/spikes/il-lowering/il-lowering-spike.csproj` (drops its own `PackageVersion`, which would now duplicate the central one), `.github/scripts/code-fingerprint.sh` and `docs/QUALITY-GATES.md` (`IL-COVERAGE.md` is now read by a test), and seven lock files, every project that references the frontend.
