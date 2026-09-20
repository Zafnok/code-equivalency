using System.Collections.Immutable;

using Equiv.Frontend.CSharp.Endpoints;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Endpoints;

/// <summary>
/// Ticket M2-005 acceptance criterion 1: detection by attribute metadata name only, so a fake attribute
/// class of the same fully-qualified name (declared right in the snippet) is enough — no dependency on
/// the real <c>Microsoft.AspNet.WebApi.Core</c>/<c>Microsoft.AspNetCore.Mvc.Core</c> packages here.
/// </summary>
public sealed class EndpointDiscoveryTests
{
    [Fact]
    public void Discover_LegacyWebApi2()
    {
        string source = """
            namespace System.Web.Http
            {
                public class RoutePrefixAttribute : System.Attribute
                {
                    public RoutePrefixAttribute(string prefix) { }
                }

                public class RouteAttribute : System.Attribute
                {
                    public RouteAttribute(string template = null) { }
                }

                public class HttpGetAttribute : System.Attribute { }
                public class HttpPostAttribute : System.Attribute { }
            }

            namespace Sample.Controllers
            {
                using System.Web.Http;

                [RoutePrefix("api/orders")]
                public class OrdersController
                {
                    [HttpGet, Route("{id:int}")]
                    public int Get(int id) => id;

                    [HttpPost, Route("")]
                    public void Post() { }

                    [HttpGet]
                    public int Count() => 0;

                    public void Helper() { }
                }
            }
            """;

        ImmutableArray<Endpoint> endpoints = EndpointDiscovery.Discover(RoslynTestCompilations.Compile(source));

        Assert.Equal(3, endpoints.Length);
        Assert.Contains(endpoints, e => IsEndpoint(e, "GET", "/api/orders/{id}"));
        Assert.Contains(endpoints, e => IsEndpoint(e, "POST", "/api/orders"));
        Assert.Contains(endpoints, e => IsEndpoint(e, "GET", "/api/orders") && e.Action.Value.Contains("Count", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_LegacyMvc5()
    {
        string source = """
            namespace System.Web.Mvc
            {
                public class RouteAttribute : System.Attribute
                {
                    public RouteAttribute(string template = null) { }
                }

                public class HttpPostAttribute : System.Attribute { }
            }

            namespace Sample.Controllers
            {
                using System.Web.Mvc;

                [Route("orders")]
                public class OrdersController
                {
                    [Route("edit/{id}")]
                    public void Edit(int id) { }

                    [HttpPost, Route("save/{id}")]
                    public void Save(int id) { }
                }
            }
            """;

        ImmutableArray<Endpoint> endpoints = EndpointDiscovery.Discover(RoslynTestCompilations.Compile(source));

        Assert.Equal(2, endpoints.Length);
        // No verb attribute on Edit: criterion 2's "actions with no verb attribute get GET".
        Assert.Contains(endpoints, e => IsEndpoint(e, "GET", "/orders/edit/{id}"));
        Assert.Contains(endpoints, e => IsEndpoint(e, "POST", "/orders/save/{id}"));
    }

    [Fact]
    public void Discover_AspNetCore()
    {
        string source = """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class RouteAttribute : System.Attribute
                {
                    public RouteAttribute(string template = null) { }
                }

                public class ApiControllerAttribute : System.Attribute { }

                public class HttpGetAttribute : System.Attribute
                {
                    public HttpGetAttribute(string template = null) { }
                }
            }

            namespace Sample.Controllers
            {
                using Microsoft.AspNetCore.Mvc;

                [ApiController]
                [Route("api/[controller]")]
                public class OrdersController
                {
                    [HttpGet("{id}")]
                    public int Get(int id) => id;
                }
            }
            """;

        ImmutableArray<Endpoint> endpoints = EndpointDiscovery.Discover(RoslynTestCompilations.Compile(source));

        Endpoint endpoint = Assert.Single(endpoints);
        Assert.Equal("GET", endpoint.Verb);
        // No separate [Route] on the action: criterion 1's "with optional template" on the verb attribute itself.
        Assert.Equal("/api/orders/{id}", endpoint.Template);
    }

    [Fact]
    public void Discover_SkipsActionsWithoutRoute()
    {
        string source = """
            namespace System.Web.Http
            {
                public class RouteAttribute : System.Attribute
                {
                    public RouteAttribute(string template = null) { }
                }

                public class HttpGetAttribute : System.Attribute { }
            }

            namespace Sample.Controllers
            {
                using System.Web.Http;

                public class OrdersController
                {
                    [HttpGet, Route("{id}")]
                    public int Get(int id) => id;

                    public void Delete(int id) { }
                }
            }
            """;

        ImmutableArray<Endpoint> endpoints = EndpointDiscovery.Discover(RoslynTestCompilations.Compile(source));

        Endpoint endpoint = Assert.Single(endpoints);
        Assert.Equal("/{id}", endpoint.Template);
    }

    [Fact]
    public void NullCompilationThrows() =>
        Assert.Throws<ArgumentNullException>(static () => EndpointDiscovery.Discover(null!));

    private static bool IsEndpoint(Endpoint endpoint, string verb, string template) =>
        string.Equals(endpoint.Verb, verb, StringComparison.Ordinal) && string.Equals(endpoint.Template, template, StringComparison.Ordinal);
}
