# M2-002 Symbol enumeration and Added/Removed end to end
Status: todo
Effort: M
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-001

## Goal
The C# frontend turns two loaded solutions into a `MatchResult` by enumerating
procedures on both sides and handing the identity sets to the M1-003 matcher. With
this ticket `equiv compare` on the samples produces real EQ004/EQ005 results; matched
pairs still get no verdict (the backend is not wired until M3).

## Spec references
VERIFICATION-MODEL.md section 4 (identity); ARCHITECTURE.md frontend step 2.

## Acceptance criteria (all must hold; nothing beyond them)
1. `ProcedureEnumerator.Enumerate(Compilation) -> ImmutableArray<EnumeratedProcedure>`
   where `EnumeratedProcedure(ProcedureIdentity Identity, IMethodSymbol Symbol, Location Location)`.
   Included: ordinary methods, constructors, property get/set accessors, operators,
   local functions are NOT included, lambdas NOT included, compiler-generated members
   NOT included (`IsImplicitlyDeclared`), abstract and extern members NOT included
   (no body).
2. `ProcedureIdentity` value format is produced by one helper,
   `RoslynIdentity.Of(IMethodSymbol)`, and is `Namespace.Type::Member(ParamType1,ParamType2)`
   using `SymbolDisplayFormat.FullyQualifiedFormat` minus the `global::` prefix, with
   generic arity as `` `n `` on types and methods and `ref`/`out` prefixes on parameter
   types. Accessors are `get_X`/`set_X`. This is the format M1-003's normaliser already
   accepts; do not change the normaliser.
3. `CSharpFrontend : ILanguageFrontend` with `Language == "csharp"`, `Supports` true for
   `.sln` and `.slnx`, `Analyze` = load both sides (M2-001), enumerate, apply the config
   rename map, call `IProcedureMatcher`, return its `MatchResult`. Loader failures
   surface as `FrontendLoadException`.
4. `Program.Main` registers `CSharpFrontend`. Running `equiv compare` on
   `samples/identical` exits 0 with a SARIF containing zero results; on a sample where a
   method exists on one side only, the SARIF has the EQ004/EQ005 result with the
   correct file and line on the side that has it.
5. Every result's `physicalLocation` points at the declaring file and the identifier
   line of the member (`Symbol.Locations[0]`).

## Files
`src/Equiv.Frontend.CSharp/ProcedureEnumerator.cs`, `EnumeratedProcedure.cs`,
`RoslynIdentity.cs`, `CSharpFrontend.cs`; one-line change in `src/Equiv.Cli/Program.cs`.

## Tests
`Equiv.Frontend.CSharp.Tests` with `AdhocWorkspace` snippets: `Enumerate_IncludesMethodsCtorsAccessorsOperators`,
`Enumerate_ExcludesLocalFunctionsLambdasImplicitAbstractExtern`,
`Identity_FormatsGenericsRefOutAndAccessors` (table test with at least 8 rows),
`Analyze_AppliesRenameMapBeforeMatching`, `Analyze_WrapsLoaderFailure`.
`Equiv.Tests.Integration`: `Samples_IdenticalYieldsNoResults`,
`Samples_AddedAndRemovedHaveLocations` (add a tiny `added-removed` sample pair: one
method only on legacy, one only on modern). Snapshot of that SARIF.

## Size guard
More than 5 new files in `src/` or more than 12 tests means scope creep.

## Out of scope
Lowering (M2-003). Endpoints (M2-005). Overload disambiguation beyond exact identity.

## Notes
