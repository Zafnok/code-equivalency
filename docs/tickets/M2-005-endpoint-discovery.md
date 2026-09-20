# M2-005 Endpoint discovery
Status: in-progress
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M2-002

## Goal
Controller actions on both sides get a second identity, the HTTP route, so a Web API 2
action and its ASP.NET Core rewrite match even though their C# identities differ.
Endpoint identity is used only for matching; verification is unchanged.

## Spec references
VERIFICATION-MODEL.md section 3 (route normalisation), section 4; ARCHITECTURE.md
frontend step 3.

## Acceptance criteria (all must hold; nothing beyond them)
1. `EndpointDiscovery.Discover(Compilation) -> ImmutableArray<Endpoint>` with
   `Endpoint(string Verb, string Template, ProcedureIdentity Action)`. Detection by
   attribute metadata name only, never by base class:
   legacy: `System.Web.Http.RoutePrefixAttribute`, `System.Web.Http.RouteAttribute`,
   `System.Web.Http.Http{Get,Post,Put,Delete,Patch}Attribute`, and
   `System.Web.Mvc.RouteAttribute` / `System.Web.Mvc.Http*Attribute`;
   modern: `Microsoft.AspNetCore.Mvc.RouteAttribute`,
   `Microsoft.AspNetCore.Mvc.Http{Get,Post,Put,Delete,Patch}Attribute` with optional template.
2. Template normalisation: prefix + route joined with one `/`, leading `/` added,
   trailing `/` removed, lower-cased, `[controller]` and `[action]` tokens replaced
   from the type and method names (`FooController` -> `foo`), parameter constraints
   stripped (`{id:int}` -> `{id}`), parameter names kept. Verb upper-cased. Actions with
   no verb attribute get `GET`. Actions with no attribute route are skipped
   (convention-based routing is out of scope).
3. `MatchResult` gains nothing; instead `CSharpFrontend.Analyze` builds a rename map
   entry `oldIdentity -> newIdentity` for every endpoint whose `(Verb, Template)` is
   present on exactly one action per side, and feeds it to the matcher ahead of the
   user's rename map (user entries win on conflict). Endpoints with duplicates on a
   side are ignored and reported as an `Unknown(UnknownReason.UnmatchedOverload)`
   result for each action involved.
4. SARIF: results for endpoint-matched procedures carry a `logicalLocations` entry with
   `kind: "endpoint"` and `fullyQualifiedName: "<VERB> <template>"`.
5. New sample `webapi-basic`: legacy `OrdersController : ApiController` with
   `[RoutePrefix("api/orders")]` and `[HttpGet, Route("{id:int}")] Get(int id)`;
   modern `[ApiController][Route("api/[controller]")] OrdersController : ControllerBase`
   with `[HttpGet("{id}")] Get(int id)`. Both class libraries (no web SDK, see M2-001
   pitfalls); reference the attributes via the `Microsoft.AspNet.WebApi.Core` and
   `Microsoft.AspNetCore.Mvc.Core` packages respectively. README states the pair matches.

## Files
`src/Equiv.Frontend.CSharp/Endpoints/EndpointDiscovery.cs`, `Endpoint.cs`,
`RouteTemplate.cs` (normalisation, pure); a small change in `CSharpFrontend.cs`;
`samples/webapi-basic/**`.

## Tests
`RouteTemplate_Normalises` table test with at least 12 rows covering every rule in
criterion 2; `Discover_LegacyWebApi2`, `Discover_LegacyMvc5`, `Discover_AspNetCore`,
`Discover_SkipsActionsWithoutRoute`, `Analyze_EndpointRenameMapBeforeUserMap`,
`Analyze_DuplicateEndpointYieldsUnknown`; integration snapshot for `webapi-basic`.

## Size guard
Three source files plus one edit. More means scope creep.

## Out of scope
Convention-based routing, areas, minimal APIs, `IHttpActionResult` vs `IActionResult`
result identity (that is a lowering concern, later), query-string binding.

## Notes
- Decision: attribute detection is an exact metadata-name lookup (`AttributeClass.ToDisplayString(FullyQualifiedFormat)` minus `global::`, mirroring `RoslynIdentity`) against closed sets/dictionaries for prefix, route and verb attributes. Alternatives: symbol comparison against a resolved `INamedTypeSymbol` (rejected: would require the real WebApi/AspNetCore assemblies to be resolvable even in unit tests, which criterion 1's "by attribute metadata name only" rules out anyway). Rule: 1 (mirrors the consumer: criterion 1 is already phrased as a name list).
- Decision: `Endpoint.Action` is the raw (unrenamed) member identity, `RoslynIdentity.Of(method, RenameMap.Empty)` — same convention `ProcedureEnumerator`/`EnumeratedProcedure` already use (M2-002) so `EndpointDiscovery.Discover(Compilation)` needs no `RenameMap` parameter, matching criterion 1's signature exactly. Rule 1.
- Decision: the "rename map entry oldIdentity -> newIdentity" (criterion 3) is implemented by substituting BOTH sides' identity with the *same* shared identity `ProcedureIdentityNormalizer.Endpoint(verb, template)` before calling `IProcedureMatcher.Match`, for every `(Verb, Template)` unique on both sides. A pair's `Old`/`New` must carry an equal `Value` (see `ProcedurePair`'s own doc comment), so only substituting the legacy side cannot work; substituting both to the shared endpoint identity is also what makes SARIF criterion 4's `fullyQualifiedName: "<VERB> <template>"` fall out of the existing `Identity.Value` with no new field on `MatchResult`/`ProcedurePair`/`VerificationResult`. Alternatives: old->new(member-identity) substitution on the legacy side only (rejected: pairs would carry the plain C# signature, not `VERB /template`, breaking criterion 4 without extending a Core record, which criterion 3 forbids). Rule 1.
- Decision: "ahead of the user's rename map (user entries win on conflict)" is implemented per-symbol: the endpoint substitution is skipped whenever applying the config's rename map actually changes that symbol's identity (`RoslynIdentity.Of(symbol, config.Renames) != RoslynIdentity.Of(symbol, RenameMap.Empty)`), i.e. whenever a real user rename fired for it. In practice this only ever gates the legacy side, since `RenameMap.Types`/`Namespaces` keys are legacy names and never match on the modern side. Alternatives: an explicit precedence table in `equiv.config.json` (rejected: new config surface, not asked for). Rule 4 (smallest change that gives the user an escape hatch).
- Decision: a `(Verb, Template)` with duplicates on one side but present on both sides is pulled out of normal identity matching entirely (excluded from what is fed to `IProcedureMatcher.Match`) and each of its actions (both sides) is appended straight into the final `MatchResult.Ambiguous` array, reusing that existing bucket rather than adding a field (criterion 3 says "MatchResult gains nothing"; `Ambiguous` -> `Unknown(UnmatchedOverload)` SARIF wiring is M3-003, already deferred repo-wide per ROADMAP.md, so this ticket does not need to and does not produce an actual SARIF `Unknown` result for these). A `(Verb, Template)` present on only one side is left alone (no rename, no forced-ambiguous) since there is nothing to conflict with; it falls through to ordinary Added/Removed handling. Rule 1 (mirrors `StableIdentityMatcher`'s own inOld/inNew branching).
- Decision (scope note, beyond this ticket's Files list): criterion 4's SARIF `logicalLocations` cannot be produced from `CSharpFrontend.cs` alone — `Result.LogicalLocations` does not exist in the SARIF SDK, only `Location.LogicalLocations` does, and nothing before `Equiv.Core.Reporting.SarifReportWriter` ever touches a SARIF `Result`. A ~10-line addition to `SarifReportWriter.ToResult` (detect an endpoint-shaped `Identity.Value` via a new `ProcedureIdentityNormalizer.IsEndpoint` helper, add a `LogicalLocation { Kind = "endpoint", FullyQualifiedName = Identity.Value }`) is the minimal way to satisfy an explicit, numbered acceptance criterion; it stays inside `Equiv.Core`'s own already-owned SARIF-emission responsibility (ARCHITECTURE.md) and adds no new dependency edge. Rule 1.
- Decision: route-template resolution precedence per action — an explicit method-level `RouteAttribute` template wins; failing that, the verb attribute's own inline template argument (modern only; legacy verb attributes never carry one, matching criterion 1's "modern: ...with optional template" wording); failing that, empty. Type-level prefix: `RoutePrefixAttribute` (legacy only) wins over a type-level `RouteAttribute` (used by both MVC5 attribute routing and ASP.NET Core controllers). An action with neither a method-level `RouteAttribute` nor a verb attribute is skipped per criterion 2. Rule 4 (smallest rule set that covers the sample and the Tests list; multi-route actions are not in either).
- Toolchain: `webapi-basic/legacy`'s `PackageReference` (`Microsoft.AspNet.WebApi.Core`) only resolves to real `<Reference>` items when built by full MSBuild (VS Build Tools' `MSBuild.exe`, what the out-of-process build host actually uses per M2-001) — a non-SDK-format csproj has no SDK targets to turn `project.assets.json` into references. `dotnet build`/`dotnet restore` against the sample directly restores fine but the *build* silently produces unresolved-type errors, because the .NET SDK's own MSBuild toolset doesn't ship that legacy compatibility import either. Not a code change; just a note for whoever next touches this sample by hand outside the test suite.
- `samples/**` is never part of `Equiv.slnx`, so `build.ps1`'s top-level `dotnet restore --locked-mode` never touches it (pre-existing, not new to this ticket) — `identical`/`added-removed`/etc. never needed restoring because they carry no `PackageReference`. `webapi-basic` is the first sample that does; its restore happens implicitly the first time something builds it (a manual `dotnet build`, or MSBuildWorkspace's own build host during a test run) and the resulting `obj`/`bin` are gitignored, same as every other sample.
- Endpoint discovery's own `AllTypes` walks only namespace-scoped types (no nested-type descent, unlike `ProcedureEnumerator`): a controller is never a nested class in practice, and this keeps the new file free of a recursion branch nothing in this ticket would otherwise exercise.
- Test-only addition: `RoslynTestCompilations.Compile` gained an `extraReferences` overload (`RoslynTestCompilations.cs`, not itself a `src/` file) so a snippet's fake route-attribute classes can live in a separate referenced compilation instead of being inlined — inlined, their own explicit constructors would otherwise show up as ordinary procedures for `ProcedureEnumerator`/`CSharpFrontend.Analyze` to enumerate and match, polluting `Added`/`Removed` in `CSharpFrontendTests`'s new endpoint tests.
- Verified: `./build.ps1 -Integration` is fully green (build, format, all seven test projects, 100% line+branch coverage on every non-empty `src/` assembly, architecture rules) with `webapi-basic` loaded through the real MSBuild pipeline (not fake attributes) matching its one action by endpoint route alone.
