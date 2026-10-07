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
              "timeoutMs": 10000,
              "resourceLimit": 123456
            }
            """;

        EquivConfigResult result = EquivConfigLoader.Load(Json);
        Assert.True(result.IsValid);
        Assert.Equal("New.Ns", result.Config.Renames.Namespaces["Old.Ns"]);
        Assert.Equal("New.Ns.Bar", result.Config.Renames.Types["Old.Ns.Foo"]);
        Assert.Equal("New.Ns.Bar::M", result.Config.CallIdentityRenames["Old.Ns.Foo::M"]);
        Assert.Equal(5, result.Config.Bound);
        Assert.Equal(10000, result.Config.TimeoutMs);
        Assert.Equal(123456, result.Config.ResourceLimit);
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

    /// <summary>Ticket P2-050 criterion 3: <c>resourceLimit</c> is a positive integer, validated as <c>timeoutMs</c> is.</summary>
    [Theory]
    [InlineData("""{ "resourceLimit": "plenty" }""")]
    [InlineData("""{ "resourceLimit": 1.5 }""")]
    [InlineData("""{ "resourceLimit": 0 }""")]
    [InlineData("""{ "resourceLimit": -1 }""")]
    [InlineData("""{ "resourceLimit": 4294967296 }""")]
    public void ResourceLimit_IsValidated(string json)
    {
        EquivConfigResult result = EquivConfigLoader.Load(json);
        Assert.Equal(new EquivConfigDiagnostic("CFG010", "/resourceLimit", "\"resourceLimit\" must be a positive integer"), Assert.Single(result.Diagnostics));
        Assert.Equal(EquivConfig.DefaultResourceLimit, result.Config.ResourceLimit);
        Assert.Equal(EquivConfig.DefaultResourceLimit, EquivConfig.Default.ResourceLimit);
        Assert.Equal(1, EquivConfigLoader.Load("""{ "resourceLimit": 1 }""").Config.ResourceLimit);
    }

    /// <summary>
    /// Ticket P1-032 criterion 1 as ADR 0052 amends it: the mode is quick unless <c>mode</c> says thorough, and the first
    /// pass's resource limit is 2,000,000 in either.
    /// </summary>
    [Fact]
    public void Mode_DefaultsToQuick()
    {
        Assert.Equal(CompareMode.Quick, EquivConfig.Default.Mode);
        Assert.Equal(CompareMode.Quick, default(CompareMode));
        Assert.Equal(CompareMode.Quick, EquivConfigLoader.Load("{}").Config.Mode);
        Assert.Equal(CompareMode.Thorough, EquivConfigLoader.Load("""{ "mode": "thorough" }""").Config.Mode);
        Assert.Equal(CompareMode.Quick, EquivConfigLoader.Load("""{ "mode": "quick" }""").Config.Mode);
        Assert.True(EquivConfigLoader.Load("""{ "mode": "thorough" }""").IsValid);
        Assert.Equal((3, 2_000_000, 60_000), (EquivConfig.Default.Bound, EquivConfig.Default.ResourceLimit, EquivConfig.Default.TimeoutMs));
        Assert.Equal(new Escalation(8, 30_000_000, 600_000), EquivConfig.Default.Escalation);
    }

    /// <summary>Criterion 1: any other <c>mode</c> is CFG013, which <c>equiv compare</c> ends on with exit 3.</summary>
    [Theory]
    [InlineData("""{ "mode": "fast" }""")]
    [InlineData("""{ "mode": "Quick" }""")]
    [InlineData("""{ "mode": 1 }""")]
    [InlineData("""{ "mode": null }""")]
    public void Mode_UnknownValue_IsReported(string json)
    {
        EquivConfigResult result = EquivConfigLoader.Load(json);

        Assert.Equal(new EquivConfigDiagnostic("CFG013", "/mode", "\"mode\" must be \"thorough\" or \"quick\""), Assert.Single(result.Diagnostics));
        Assert.Equal(CompareMode.Quick, result.Config.Mode);
        Assert.Null(EquivConfigLoader.ParseMode(name: null));
    }

    /// <summary>
    /// Criterion 3 (ADR 0049 decision 4): <c>escalation</c> replaces the budget pass's values key by key, and the config
    /// remembers which settings the file gave, in a fixed order.
    /// </summary>
    [Fact]
    public void Escalation_ReplacesTheBudgetPassValuesAndExplicitSettingsAreRemembered()
    {
        EquivConfig partly = EquivConfigLoader.Load("""{ "escalation": { "resourceLimit": 9 } }""").Config;
        EquivConfigResult whole = EquivConfigLoader.Load("""{ "timeoutMs": 7, "escalation": { "timeoutMs": 3, "bound": 4, "resourceLimit": 5 }, "resourceLimit": 6, "bound": 2 }""");

        Assert.Equal(new Escalation(8, 9, 600_000), partly.Escalation);
        Assert.Equal(["escalation"], partly.Explicit);
        Assert.True(whole.IsValid);
        Assert.Equal(new Escalation(4, 5, 3), whole.Config.Escalation);
        Assert.Equal(["bound", "resourceLimit", "timeoutMs", "escalation"], whole.Config.Explicit);
        Assert.Empty(EquivConfigLoader.Load("{}").Config.Explicit);
        Assert.Equal(["bound"], EquivConfig.Default.WithExplicit("bound", given: true).WithExplicit("bound", given: true).WithExplicit("timeoutMs", given: false).Explicit);
    }

    /// <summary>Criterion 3: an <c>escalation</c> value that is not a positive integer, or a key it does not have, is CFG014 and keeps ADR 0049's value.</summary>
    [Theory]
    [InlineData("""{ "escalation": 8 }""", "/escalation", "\"escalation\" must be an object with \"bound\", \"resourceLimit\" and/or \"timeoutMs\"")]
    [InlineData("""{ "escalation": { "bound": 0 } }""", "/escalation/bound", "\"bound\" must be a positive integer")]
    [InlineData("""{ "escalation": { "resourceLimit": "plenty" } }""", "/escalation/resourceLimit", "\"resourceLimit\" must be a positive integer")]
    [InlineData("""{ "escalation": { "timeoutMs": 4294967296 } }""", "/escalation/timeoutMs", "\"timeoutMs\" must be a positive integer")]
    [InlineData("""{ "escalation": { "jobs": 2 } }""", "/escalation/jobs", "unknown property \"jobs\"")]
    public void Escalation_IsValidated(string json, string path, string message)
    {
        EquivConfigResult result = EquivConfigLoader.Load(json);

        Assert.Equal(new EquivConfigDiagnostic("CFG014", path, message), Assert.Single(result.Diagnostics));
        Assert.Equal(Escalation.Default, result.Config.Escalation);
    }

    /// <summary>ADR 0049 decision 4: the budget pass never asks with less than the first pass.</summary>
    [Fact]
    public void Escalation_NeverBelowFirstPass()
    {
        Assert.Equal(new Escalation(8, 30_000_000, 600_000), Escalation.Default.AtLeast(3, 2_000_000, 60_000));
        Assert.Equal(new Escalation(20, 40_000_000, 700_000), Escalation.Default.AtLeast(20, 40_000_000, 700_000));
        Assert.Equal(new Escalation(8, 40_000_000, 600_000), Escalation.Default.AtLeast(3, 40_000_000, 60_000));
    }

    /// <summary>Ticket P2-077 criterion 1: <c>jobs</c> is a positive integer, validated as <c>timeoutMs</c> is, and one unless set.</summary>
    [Theory]
    [InlineData("""{ "jobs": "many" }""")]
    [InlineData("""{ "jobs": 1.5 }""")]
    [InlineData("""{ "jobs": 0 }""")]
    [InlineData("""{ "jobs": -1 }""")]
    [InlineData("""{ "jobs": 4294967296 }""")]
    public void Jobs_IsValidated(string json)
    {
        EquivConfigResult result = EquivConfigLoader.Load(json);
        Assert.Equal(new EquivConfigDiagnostic("CFG012", "/jobs", "\"jobs\" must be a positive integer"), Assert.Single(result.Diagnostics));
        Assert.Equal(1, result.Config.Jobs);
        Assert.Equal(1, EquivConfig.Default.Jobs);
        Assert.Equal(3, EquivConfigLoader.Load("""{ "jobs": 3 }""").Config.Jobs);
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

    /// <summary>Ticket P2-053 acceptance criterion 4: <c>runtimes</c> takes a target framework per side, and anything else is CFG009.</summary>
    [Fact]
    public void Runtimes_AreValidated()
    {
        EquivConfigResult both = EquivConfigLoader.Load("""{ "runtimes": { "legacy": ".NETFramework,Version=v4.8", "modern": "net8.0" } }""");
        Assert.True(both.IsValid);
        Assert.Equal(TargetRuntime.Parse("net48"), both.Config.LegacyRuntime);
        Assert.Equal(TargetRuntime.Parse("net8.0"), both.Config.ModernRuntime);

        EquivConfigResult none = EquivConfigLoader.Load("{}");
        Assert.Null(none.Config.LegacyRuntime);
        Assert.Null(none.Config.ModernRuntime);

        EquivConfigResult invalid = EquivConfigLoader.Load("""{ "runtimes": { "legacy": "netstandard2.0", "modern": "net10.0", "other": "net48" } }""");
        Assert.Equal(
            [new EquivConfigDiagnostic("CFG009", "/runtimes/legacy", "value must be a .NET Framework or .NET target framework, such as \"net48\" or \"net8.0\""),
             new EquivConfigDiagnostic("CFG009", "/runtimes/other", "unknown side \"other\" (expected \"legacy\" or \"modern\")")],
            invalid.Diagnostics);
        Assert.Null(invalid.Config.LegacyRuntime);
        Assert.Equal(TargetRuntime.Parse("net10.0"), invalid.Config.ModernRuntime);
    }

    /// <summary>Ticket P1-033 (ADR 0050 decision 6): <c>solvers.cvc5.path</c> names the executable; absent, no solver is configured.</summary>
    [Fact]
    public void Cvc5Path_IsRead()
    {
        EquivConfigResult set = EquivConfigLoader.Load("""{ "solvers": { "cvc5": { "path": "tools/cvc5/cvc5.exe" } } }""");
        Assert.True(set.IsValid);
        Assert.Equal("tools/cvc5/cvc5.exe", set.Config.Cvc5Path);
        Assert.Equal(set.Config, EquivConfig.Default with { Cvc5Path = "tools/cvc5/cvc5.exe" });
        Assert.Equal(set.Config.GetHashCode(), (EquivConfig.Default with { Cvc5Path = "tools/cvc5/cvc5.exe" }).GetHashCode());
        Assert.NotEqual(set.Config.GetHashCode(), EquivConfig.Default.GetHashCode());

        Assert.Null(EquivConfigLoader.Load("{}").Config.Cvc5Path);
        Assert.Null(EquivConfig.Default.Cvc5Path);
        EquivConfigResult empty = EquivConfigLoader.Load("""{ "solvers": { "cvc5": {} } }""");
        Assert.True(empty.IsValid);
        Assert.Null(empty.Config.Cvc5Path);
        Assert.True(EquivConfigLoader.Load("""{ "solvers": {} }""").IsValid);
    }

    /// <summary>Anything under <c>solvers</c> but <c>cvc5.path</c> as a non-empty string is CFG011, and what is valid beside it is kept.</summary>
    [Fact]
    public void Solvers_AreValidated()
    {
        EquivConfigResult invalid = EquivConfigLoader.Load("""{ "solvers": { "bitwuzla": { "path": "b" }, "cvc5": { "rlimit": 5, "path": "cvc5" } } }""");

        Assert.Equal(
            [new EquivConfigDiagnostic("CFG011", "/solvers/bitwuzla", "unknown solver \"bitwuzla\" (expected \"cvc5\")"),
             new EquivConfigDiagnostic("CFG011", "/solvers/cvc5/rlimit", "unknown property \"rlimit\" (expected \"path\")")],
            invalid.Diagnostics);
        Assert.Equal("cvc5", invalid.Config.Cvc5Path);
        Assert.Equal("CFG011", EquivConfigDiagnosticIds.InvalidSolvers);
    }

    /// <summary>Every diagnostic's exact id, JSON-pointer path and message, so a report names the offending entry.</summary>
    [Theory]
    [InlineData("""[]""", "CFG001", "/", "equiv.config.json must contain a JSON object")]
    [InlineData("""{ "bogus": 1 }""", "CFG005", "/bogus", "unknown property \"bogus\"")]
    [InlineData("""{ "typeRenames": 1 }""", "CFG004", "/typeRenames", "\"typeRenames\" must be an object of string to string")]
    [InlineData("""{ "typeRenames": { "A": 1 } }""", "CFG004", "/typeRenames/A", "value must be a string")]
    [InlineData("""{ "typeRenames": { " ": "B" } }""", "CFG004", "/typeRenames/ ", "key must not be empty")]
    [InlineData("""{ "typeRenames": { "A": " " } }""", "CFG004", "/typeRenames/A", "value must not be empty")]
    [InlineData("""{ "typeRenames": { "A": "B", "A": "C" } }""", "CFG006", "/typeRenames/A", "duplicate key \"A\" (JSON keeps only the last one)")]
    [InlineData("""{ "suppressApiEquivalences": 1 }""", "CFG008", "/suppressApiEquivalences", "\"suppressApiEquivalences\" must be an array of non-empty strings")]
    [InlineData("""{ "suppressApiEquivalences": ["a", "b", ""] }""", "CFG008", "/suppressApiEquivalences/2", "value must be a non-empty string")]
    [InlineData("""{ "bound": "x" }""", "CFG002", "/bound", "\"bound\" must be a positive integer")]
    [InlineData("""{ "bound": 1.5 }""", "CFG002", "/bound", "\"bound\" must be a positive integer")]
    [InlineData("""{ "runtimes": "net48" }""", "CFG009", "/runtimes", "\"runtimes\" must be an object with \"legacy\" and/or \"modern\"")]
    [InlineData("""{ "runtimes": { "modern": 8 } }""", "CFG009", "/runtimes/modern", "value must be a .NET Framework or .NET target framework, such as \"net48\" or \"net8.0\"")]
    [InlineData("""{ "solvers": "cvc5" }""", "CFG011", "/solvers", "\"solvers\" must be an object with \"cvc5\"")]
    [InlineData("""{ "solvers": { "cvc5": "cvc5.exe" } }""", "CFG011", "/solvers/cvc5", "\"cvc5\" must be an object with \"path\"")]
    [InlineData("""{ "solvers": { "cvc5": { "path": 5 } } }""", "CFG011", "/solvers/cvc5/path", "value must be a non-empty string")]
    [InlineData("""{ "solvers": { "cvc5": { "path": " " } } }""", "CFG011", "/solvers/cvc5/path", "value must be a non-empty string")]
    [InlineData("""{ "timeoutMs": 0 }""", "CFG003", "/timeoutMs", "\"timeoutMs\" must be a positive integer")]
    public void EachDiagnosticNamesItsPathAndProblem(string json, string id, string path, string message) =>
        Assert.Equal(new EquivConfigDiagnostic(id, path, message), Assert.Single(EquivConfigLoader.Load(json).Diagnostics));
}
