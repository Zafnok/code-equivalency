# M2-002 Symbol enumeration and Added/Removed end to end
Status: done (PR #25)
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

- Decision (ADR 0012, accepted 2026-09-18): before M3-001 the CLI has no backend, and
  that is modelled as `null`. `CompareCommand.Create`/`Run` take `IVerificationBackend?`,
  `BuildResults` skips `matchResult.Pairs` when it is `null`, `Program.Main` passes
  `frontends: [CSharpFrontend]` and `backend: null`, and `NoBackend.cs` and
  `NoBackendTests.cs` are deleted. Add `Compare_WithoutBackend_SkipsMatchedPairs` to
  `CompareCommandTests`. Acceptance criterion 4 is unchanged. These `Equiv.Cli` edits are in
  scope for this ticket and do not count toward the size guard (no new `src/` files, and the
  net test count is unchanged). Remove the ADR 0012 comment in `Program.cs` when wiring.
- Decision: `ProcedureIdentity` gains an optional `SourceSpan? Location` (default
  `null`), excluded from `Equals`/`GetHashCode` (manual overrides, `Value` only), rather
  than changing `MatchResult.Added`/`Removed`'s element type or adding a `Location` to
  `VerificationResult`. Alternatives: a `LocatedIdentity(ProcedureIdentity, SourceSpan?)`
  wrapper for `MatchResult.Added`/`Removed` (ripples through `IProcedureMatcher`,
  `StableIdentityMatcher`, and their M1-003 tests); a `Location` field on
  `VerificationResult` (same ripple, plus `CompareCommand.BuildResults`). Rule: 4 (only
  `ProcedureIdentity` and `SarifReportWriter` needed to change; `MatchResult`,
  `IProcedureMatcher`, `StableIdentityMatcher`, and `VerificationResult` are untouched).
  `SarifReportWriter.ToResult` emits SARIF `Locations` (`PhysicalLocation`/
  `ArtifactLocation`/`Region`) only when `Identity.Location` is set.
- Decision: `RoslynIdentity.Of(IMethodSymbol, RenameMap)` (not `Of(IMethodSymbol)` alone
  as acceptance criterion 2's prose literally reads) so a single call can both format
  and rename — the alternative (an unrenamed `Of(symbol)` plus a second rename pass over
  the already-formatted string) would need the normaliser's private `Rename` step
  exposed, which the ticket goal explicitly forbids changing. `ProcedureEnumerator`
  calls it with `RenameMap.Empty` (its own signature has no rename parameter, per
  acceptance criterion 1); `CSharpFrontend.Analyze` calls it again per
  `EnumeratedProcedure.Symbol` with `config.Renames`, which is acceptance criterion 3's
  "apply the config rename map" step. Rule: 1 (mirrors the consumer: `CSharpFrontend`
  has both the symbol and the `RenameMap` together at the point it needs the final,
  match-ready identity).
- Decision: the declaring type and the method's own generic arity render as `` `n ``
  (metadata-style), but a parameter's type keeps `SymbolDisplayFormat.FullyQualifiedFormat`'s
  literal rendering, including its own generic arguments and, on this Roslyn version,
  C# keywords for special types (`int`, `string`, not `System.Int32`/`System.String`) —
  confirmed empirically via the table test, not assumed. Only the type/method-name
  position needs arity notation: it stands in for an unbound type parameter's name
  (`T` vs `TEntity`), which is not assembly-agnostic; a parameter's own type is either
  already closed (stable) or itself the method's/type's type parameter (rendered as its
  literal name, same as any other parameter type). Rule: 1 (matches the ticket's literal
  wording: `FullyQualifiedFormat` minus `global::`, arity only "on types and methods").
- Decision: included `MethodKind`s are `Ordinary`, `Constructor`, `StaticConstructor`,
  `PropertyGet`, `PropertySet`, `UserDefinedOperator`, `Conversion`; everything else
  (destructors, event accessors) is excluded — the ticket names only "ordinary methods,
  constructors, property get/set accessors, operators". `StaticConstructor` is my
  addition (a real procedure with behaviour to compare); destructors/event accessors are
  not, since nothing in the ticket asks for them. Rule: 4.
- Decision: constructor/operator member names use `IMethodSymbol.Name` unchanged
  (`.ctor`, `op_Addition`, ...), matching accessors' `get_X`/`set_X` convention the
  ticket already specifies, rather than inventing a different label. Rule: 1.
- Decision: `Equiv.Frontend.CSharp.Tests` builds test compilations via `AdhocWorkspace`
  + `CSharpCompilationOptions` + all `TRUSTED_PLATFORM_ASSEMBLIES` as references (a
  `RoslynTestCompilations` helper in the test project itself, not a shared test-support
  project — CLAUDE.md reserves `Equiv.TestSupport` for IR generators/fixtures three
  projects need). Rule: 4.
- ADR 0012 implemented per its Decision: `IVerificationBackend?` is now nullable on
  `CompareCommand.Create`/`Run`; `BuildResults` adds no result for `matchResult.Pairs`
  when `backend` is `null` (Added/Removed unaffected); `Program.Main` passes
  `frontends: [new CSharpFrontend()]` and `backend: null`; `src/Equiv.Cli/NoBackend.cs`
  and `tests/Equiv.Cli.Tests/NoBackendTests.cs` are deleted;
  `CompareCommandTests.Compare_WithoutBackend_SkipsMatchedPairs` covers the `null`
  branch. `ProgramTests`'s frontend-rejection test now exercises the `.tmp`-extension
  path instead of an empty frontend list (renamed to
  `Main_WithValidArgsButUnsupportedExtensionExits3`), since a frontend is registered now.
- Decision: `Equiv.Tests.Integration` gets `InternalsVisibleTo` on `Equiv.Cli`
  (`ComparePipelineTests` drives `CompareCommand.Run` directly, an internal contract),
  mirroring the same precedent CLAUDE.md already names for M2-001's
  `Equiv.Frontend.CSharp` -> `Equiv.Tests.Integration` grant. Also added `Verify.XunitV3`
  and `Sarif.Sdk` package references to `Equiv.Tests.Integration.csproj` (both already
  ADR-0002-approved dependencies used elsewhere) for the SARIF snapshot test. Rule: 1
  (mirrors the existing M2-001 precedent).
- Decision: `ComparePipelineTests.AddedAndRemovedHaveLocations`'s snapshot scrubs the
  absolute `SamplesRoot` prefix out of the SARIF `artifactLocation.uri` values (replaced
  with the literal placeholder `<samples>`) before calling `VerifyJson`, since that path
  is rooted at whatever directory the repo is checked out to and would make the
  `.verified.txt` machine/CI-specific otherwise. Rule: 3 (keeps the snapshot pinned and
  portable) — the resulting `.verified.txt` shows the exact rule ids, messages, and
  `region` line/column values (line 10, the `LegacyOnly`/`ModernOnly` declaration line in
  each `added-removed/*/Calculator.cs`) acceptance criteria 4 and 5 ask for.
- All acceptance criteria hold under `./build.ps1 -Integration` (100% line/branch
  coverage on every `src/` project; format, architecture, and all test projects green):
  AC1 `ProcedureEnumeratorTests` (3 tests); AC2
  `RoslynIdentityTests.FormatsGenericsRefOutAndAccessors` (11-row table); AC3
  `CSharpFrontendTests` (`AppliesRenameMapBeforeMatching`, `WrapsLoaderFailure`,
  `SupportsOnlySlnAndSlnx`, `LanguageIsCsharp`); AC4/AC5
  `Equiv.Tests.Integration.ComparePipelineTests` (`IdenticalYieldsNoResults`,
  `AddedAndRemovedHaveLocations` + its SARIF snapshot).
