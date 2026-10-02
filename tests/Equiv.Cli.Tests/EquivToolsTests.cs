using System.Collections.Immutable;
using System.Text.Json;

using Equiv.Core.Verdicts;

using ModelContextProtocol.Server;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary><see cref="EquivTools"/>' tool schemas, read without a server (ticket P2-057; ADR 0040 decision 4).</summary>
public sealed class EquivToolsTests
{
    private static readonly ImmutableDictionary<string, Verdict> NoVerdicts = [];

    /// <summary>The parameters keep the names <c>legacy</c> and <c>modern</c>; their descriptions say which solution each one is.</summary>
    [Fact]
    public void ParameterDescriptionsSayBeforeAndAfter()
    {
        IReadOnlyList<McpServerTool> tools = new EquivTools([new FakeFrontend("csharp", _ => true)], new FakeBackend(NoVerdicts)).Create();

        Assert.Equal(["compare", "lower_only"], tools.Select(static t => t.ProtocolTool.Name), StringComparer.Ordinal);
        Assert.All(tools, static tool =>
        {
            JsonElement properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
            Assert.Equal("Path to the solution before the change (.sln or .slnx).", properties.GetProperty("legacy").GetProperty("description").GetString());
            Assert.Equal("Path to the solution after it (.sln or .slnx).", properties.GetProperty("modern").GetProperty("description").GetString());
        });
    }
}
