using Equiv.Frontend.CSharp.Endpoints;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Endpoints;

/// <summary>Ticket M2-005 acceptance criterion 2: every normalisation rule, table-tested.</summary>
public sealed class RouteTemplateTests
{
    [Theory]
    [InlineData("api/orders", "{id}", "OrdersController", "Get", "/api/orders/{id}")]
    [InlineData(null, "orders", "OrdersController", "Get", "/orders")]
    [InlineData("api/orders", null, "OrdersController", "Get", "/api/orders")]
    [InlineData("/api/orders/", "/{id}/", "OrdersController", "Get", "/api/orders/{id}")]
    [InlineData("API/Orders", "{Id}", "OrdersController", "Get", "/api/orders/{id}")]
    [InlineData("api/[controller]", "{id}", "OrdersController", "Get", "/api/orders/{id}")]
    [InlineData("api/[controller]", "[action]/{id}", "OrdersController", "GetById", "/api/orders/getbyid/{id}")]
    [InlineData("api/orders", "{id:int}", "OrdersController", "Get", "/api/orders/{id}")]
    [InlineData("api/orders", "{id:int:min(1)}", "OrdersController", "Get", "/api/orders/{id}")]
    [InlineData("api/orders", "{orderId}", "OrdersController", "Get", "/api/orders/{orderid}")]
    [InlineData(null, null, "OrdersController", "Get", "/")]
    [InlineData("Api/Orders/", "/{Id:int}/", "OrdersController", "Get", "/api/orders/{id}")]
    [InlineData("[controller]", null, "Orders", "Get", "/orders")]
    public void Normalises(string? prefix, string? route, string typeName, string methodName, string expected) =>
        Assert.Equal(expected, RouteTemplate.Normalize(prefix, route, typeName, methodName));
}
