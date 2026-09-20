using Equiv.Core;

namespace Equiv.Frontend.CSharp.Endpoints;

/// <summary>
/// One HTTP route attribute-routing gives a controller action (ARCHITECTURE.md frontend step 3;
/// VERIFICATION-MODEL.md section 3): <see cref="Verb"/> is upper-cased, <see cref="Template"/> is
/// normalised (<see cref="RouteTemplate.Normalize"/>). <see cref="Action"/> is the action's own raw
/// (unrenamed) member identity — the same convention <c>EnumeratedProcedure.Identity</c> uses (M2-002)
/// — so a config rename map never needs to reach <see cref="EndpointDiscovery.Discover"/>.
/// </summary>
internal sealed record Endpoint(string Verb, string Template, ProcedureIdentity Action);
