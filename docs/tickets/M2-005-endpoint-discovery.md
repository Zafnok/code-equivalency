# M2-005 Endpoint discovery
Status: todo
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
