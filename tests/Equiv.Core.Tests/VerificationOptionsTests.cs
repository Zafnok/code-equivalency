using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;

using Xunit;

namespace Equiv.Core.Tests;

public sealed class VerificationOptionsTests
{
    [Fact]
    public void OptionsWithEquivalentCallIdentityMapsAreEqual()
    {
        VerificationOptions a = new(3, 5000, ImmutableDictionary<string, string>.Empty.Add("Old::M", "New::M"));
        VerificationOptions b = new(3, 5000, ImmutableDictionary<string, string>.Empty.Add("Old::M", "New::M"));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void OptionsWithDifferentCallIdentityMapsAreUnequal()
    {
        VerificationOptions a = new(3, 5000, []);
        VerificationOptions b = new(3, 5000, ImmutableDictionary<string, string>.Empty.Add("Old::M", "New::M"));
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(4, 5000)]
    [InlineData(3, 6000)]
    public void OptionsWithDifferentBoundOrTimeoutAreUnequal(int bound, int timeoutMs)
    {
        VerificationOptions a = new(3, 5000, []);
        VerificationOptions b = new(bound, timeoutMs, []);
        Assert.NotEqual(a, b);
    }

    /// <summary>Ticket P1-001: <c>--chc-int-mode</c> is on unless turned off, and two options differing in it are unequal.</summary>
    /// <summary>Ticket P1-002 criterion 4: rung 5 is off unless a model is named, and two options differing in it are unequal.</summary>
    [Fact]
    public void InvariantModelIsOffByDefaultAndPartOfEquality()
    {
        VerificationOptions a = new(3, 5000, []);

        Assert.Null(a.InvariantModel);
        Assert.NotEqual(a, a with { InvariantModel = "m" });
        Assert.Equal(a with { InvariantModel = "m" }, new VerificationOptions(3, 5000, []) { InvariantModel = "m" });
        Assert.Equal((a with { InvariantModel = "m" }).GetHashCode(), new VerificationOptions(3, 5000, []) { InvariantModel = "m" }.GetHashCode());
    }

    [Fact]
    public void ChcIntModeIsOnByDefaultAndPartOfEquality()
    {
        VerificationOptions a = new(3, 5000, []);

        Assert.True(a.ChcIntMode);
        Assert.NotEqual(a, a with { ChcIntMode = false });
        Assert.Equal(a with { ChcIntMode = false }, new VerificationOptions(3, 5000, []) { ChcIntMode = false });
        Assert.Equal((a with { ChcIntMode = false }).GetHashCode(), new VerificationOptions(3, 5000, []) { ChcIntMode = false }.GetHashCode());
    }

    /// <summary>Ticket P2-050: the resource limit is the config's default unless set, and two options differing in it are unequal.</summary>
    [Fact]
    public void ResourceLimitIsTheConfigDefaultAndPartOfEquality()
    {
        VerificationOptions a = new(3, 5000, []);

        Assert.Equal(EquivConfig.DefaultResourceLimit, a.ResourceLimit);
        Assert.NotEqual(a, a with { ResourceLimit = 7 });
        Assert.Equal(a with { ResourceLimit = 7 }, new VerificationOptions(3, 5000, []) { ResourceLimit = 7 });
        Assert.Equal((a with { ResourceLimit = 7 }).GetHashCode(), new VerificationOptions(3, 5000, []) { ResourceLimit = 7 }.GetHashCode());
        Assert.NotEqual((a with { ResourceLimit = 7 }).GetHashCode(), a.GetHashCode());
    }

    /// <summary>Ticket P1-033: no second solver unless one is given, and two options differing in it are unequal, since it can change a verdict.</summary>
    [Fact]
    public void SolverIsNoneByDefaultAndPartOfEquality()
    {
        VerificationOptions a = new(3, 5000, []);
        NamedSolver cvc5 = new("cvc5");

        Assert.Null(a.Solver);
        Assert.NotEqual(a, a with { Solver = cvc5 });
        Assert.NotEqual(a with { Solver = cvc5 }, a);
        Assert.NotEqual(a with { Solver = cvc5 }, a with { Solver = new NamedSolver("other") });
        Assert.Equal(a with { Solver = cvc5 }, new VerificationOptions(3, 5000, []) { Solver = new NamedSolver("cvc5") });
        Assert.Equal((a with { Solver = cvc5 }).GetHashCode(), new VerificationOptions(3, 5000, []) { Solver = new NamedSolver("cvc5") }.GetHashCode());
        Assert.NotEqual((a with { Solver = cvc5 }).GetHashCode(), a.GetHashCode());
        Assert.IsType<SmtUnknown>(cvc5.Ask("(check-sat)", TimeSpan.Zero));
        Assert.Equal("1", cvc5.Version);
    }

    [Fact]
    public void OptionsAreNotEqualToNull()
    {
        Assert.False(new VerificationOptions(3, 5000, []).Equals(Null.Of<VerificationOptions>()));
    }

    /// <summary>A solver equal to another of the same name, as a record is.</summary>
    private sealed record NamedSolver(string Name) : ISmtSolver
    {
        public string Version => "1";

        public SmtAnswer Ask(string script, TimeSpan limit) => new SmtUnknown(Name);
    }
}
