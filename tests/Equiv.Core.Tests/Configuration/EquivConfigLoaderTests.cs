using System.Collections.Immutable;

using Equiv.Core.Configuration;

using Xunit;

namespace Equiv.Core.Tests.Configuration;

public sealed class EquivConfigLoaderTests
{
    [Fact]
    public void MalformedJsonThrows()
    {
        EquivConfigParseException exception = Assert.Throws<EquivConfigParseException>(static () => EquivConfigLoader.Load("{ not json"));
        Assert.Contains("equiv.config.json is not valid JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyObjectYieldsDefaultsAndNoDiagnostics()
    {
        EquivConfigResult result = EquivConfigLoader.Load("{}");
        Assert.True(result.IsValid);
        Assert.Equal(EquivConfig.Default, result.Config);
    }

    [Fact]
    public void NonObjectRootIsInvalid()
    {
        EquivConfigResult result = EquivConfigLoader.Load("[1,2,3]");
        Assert.Equal(EquivConfig.Default, result.Config);
        Assert.Equal([EquivConfigDiagnosticIds.InvalidRoot], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
    }

    [Fact]
    public void FullyPopulatedConfigParsesEveryField()
    {
        const string Json = """
            {
              "namespaceRenames": { "Old.Ns": "New.Ns" },
              "typeRenames": { "Old.Ns.Foo": "New.Ns.Bar" },
              "callIdentityRenames": { "Old.Ns.Foo::M": "New.Ns.Bar::M" },
              "bound": 5,
              "timeoutMs": 10000
            }
            """;

        EquivConfigResult result = EquivConfigLoader.Load(Json);
        Assert.True(result.IsValid);
        Assert.Equal("New.Ns", result.Config.Renames.Namespaces["Old.Ns"]);
        Assert.Equal("New.Ns.Bar", result.Config.Renames.Types["Old.Ns.Foo"]);
        Assert.Equal("New.Ns.Bar::M", result.Config.CallIdentityRenames["Old.Ns.Foo::M"]);
        Assert.Equal(5, result.Config.Bound);
        Assert.Equal(10000, result.Config.TimeoutMs);
    }

    [Fact]
    public void UnknownTopLevelPropertyIsReported()
    {
        EquivConfigResult result = EquivConfigLoader.Load("""{ "boundd": 3 }""");
        Assert.Equal([EquivConfigDiagnosticIds.UnknownProperty], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Equal(EquivConfig.Default.Bound, result.Config.Bound);
    }

    [Theory]
    [InlineData("""{ "bound": "three" }""")]
    [InlineData("""{ "bound": 1.5 }""")]
    [InlineData("""{ "bound": 0 }""")]
    [InlineData("""{ "bound": -1 }""")]
    public void InvalidBoundFallsBackToTheDefaultAndIsReported(string json)
    {
        EquivConfigResult result = EquivConfigLoader.Load(json);
        Assert.Equal([EquivConfigDiagnosticIds.InvalidBound], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Equal(EquivConfig.Default.Bound, result.Config.Bound);
    }

    [Theory]
    [InlineData("""{ "timeoutMs": "slow" }""")]
    [InlineData("""{ "timeoutMs": 0 }""")]
    public void InvalidTimeoutFallsBackToTheDefaultAndIsReported(string json)
    {
        EquivConfigResult result = EquivConfigLoader.Load(json);
        Assert.Equal([EquivConfigDiagnosticIds.InvalidTimeout], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Equal(EquivConfig.Default.TimeoutMs, result.Config.TimeoutMs);
    }

    [Fact]
    public void RenameMapMustBeAnObject()
    {
        EquivConfigResult result = EquivConfigLoader.Load("""{ "namespaceRenames": "nope" }""");
        Assert.Equal([EquivConfigDiagnosticIds.InvalidRenameEntry], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Empty(result.Config.Renames.Namespaces);
    }

    [Fact]
    public void RenameEntryValueMustBeAString()
    {
        EquivConfigResult result = EquivConfigLoader.Load("""{ "namespaceRenames": { "Old.Ns": 5 } }""");
        Assert.Equal([EquivConfigDiagnosticIds.InvalidRenameEntry], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Empty(result.Config.Renames.Namespaces);
    }

    [Fact]
    public void RenameEntryKeyMustNotBeEmpty()
    {
        EquivConfigResult result = EquivConfigLoader.Load("""{ "typeRenames": { "": "New.Ns.Bar" } }""");
        Assert.Equal([EquivConfigDiagnosticIds.InvalidRenameEntry], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Empty(result.Config.Renames.Types);
    }

    [Fact]
    public void RenameEntryValueMustNotBeEmpty()
    {
        EquivConfigResult result = EquivConfigLoader.Load("""{ "typeRenames": { "Old.Ns.Foo": "   " } }""");
        Assert.Equal([EquivConfigDiagnosticIds.InvalidRenameEntry], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Empty(result.Config.Renames.Types);
    }

    [Fact]
    public void DuplicateRenameKeyIsReportedAndTheLastValueWins()
    {
        EquivConfigResult result = EquivConfigLoader.Load("""{ "namespaceRenames": { "Old.Ns": "First", "Old.Ns": "Second" } }""");
        Assert.Equal([EquivConfigDiagnosticIds.DuplicateRenameEntry], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Equal("Second", result.Config.Renames.Namespaces["Old.Ns"]);
    }

    [Fact]
    public void NullJsonThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(static () => EquivConfigLoader.Load(null!));
    }

    [Fact]
    public void Config_ParsesSuppressRuntimeChanges()
    {
        EquivConfigResult result = EquivConfigLoader.Load("""{ "suppressRuntimeChanges": ["System.String::IndexOf(", "System.String::GetHashCode("] }""");

        Assert.True(result.IsValid);
        Assert.Equal(["System.String::IndexOf(", "System.String::GetHashCode("], result.Config.SuppressRuntimeChanges);
    }

    [Fact]
    public void MissingSuppressRuntimeChangesYieldsAnEmptyArray()
    {
        EquivConfigResult result = EquivConfigLoader.Load("{}");
        Assert.Empty(result.Config.SuppressRuntimeChanges);
    }

    [Theory]
    [InlineData("""{ "suppressRuntimeChanges": "nope" }""")]
    [InlineData("""{ "suppressRuntimeChanges": [""] }""")]
    [InlineData("""{ "suppressRuntimeChanges": ["   "] }""")]
    [InlineData("""{ "suppressRuntimeChanges": [5] }""")]
    public void InvalidSuppressRuntimeChangesEntryFallsBackToEmptyAndIsReported(string json)
    {
        EquivConfigResult result = EquivConfigLoader.Load(json);
        Assert.Equal([EquivConfigDiagnosticIds.InvalidSuppressRuntimeChangesEntry], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Empty(result.Config.SuppressRuntimeChanges);
    }

    [Fact]
    public void ASuppressRuntimeChangesEntryThatIsValidIsKeptEvenWhenAnotherEntryIsInvalid()
    {
        EquivConfigResult result = EquivConfigLoader.Load("""{ "suppressRuntimeChanges": ["System.String::IndexOf(", ""] }""");

        Assert.Equal([EquivConfigDiagnosticIds.InvalidSuppressRuntimeChangesEntry], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Equal(["System.String::IndexOf("], result.Config.SuppressRuntimeChanges);
    }

    [Fact]
    public void Config_SuppressApiEquivalences_IsParsed()
    {
        EquivConfigResult result = EquivConfigLoader.Load("""{ "suppressApiEquivalences": ["webapi.", "bcl.string-split-one-char"] }""");

        Assert.True(result.IsValid);
        Assert.Equal(["webapi.", "bcl.string-split-one-char"], result.Config.SuppressApiEquivalences);
    }

    [Fact]
    public void Config_SuppressApiEquivalences_DefaultsToEmpty()
    {
        EquivConfigResult result = EquivConfigLoader.Load("{}");
        Assert.Empty(result.Config.SuppressApiEquivalences);
    }

    [Theory]
    [InlineData("""{ "suppressApiEquivalences": "nope" }""")]
    [InlineData("""{ "suppressApiEquivalences": [""] }""")]
    [InlineData("""{ "suppressApiEquivalences": ["   "] }""")]
    [InlineData("""{ "suppressApiEquivalences": [5] }""")]
    public void Config_SuppressApiEquivalences_InvalidEntryFallsBackToEmptyAndIsReported(string json)
    {
        EquivConfigResult result = EquivConfigLoader.Load(json);
        Assert.Equal([EquivConfigDiagnosticIds.InvalidSuppressApiEquivalencesEntry], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Empty(result.Config.SuppressApiEquivalences);
    }

    [Fact]
    public void Config_SuppressApiEquivalences_KeepsAValidEntryWhenAnotherIsInvalid()
    {
        EquivConfigResult result = EquivConfigLoader.Load("""{ "suppressApiEquivalences": ["webapi.", ""] }""");

        Assert.Equal([EquivConfigDiagnosticIds.InvalidSuppressApiEquivalencesEntry], result.Diagnostics.Select(static d => d.Id), StringComparer.Ordinal);
        Assert.Equal(["webapi."], result.Config.SuppressApiEquivalences);
    }
}
