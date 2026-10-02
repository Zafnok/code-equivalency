using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.RuntimeChanges;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Core.Tests.Reporting;

/// <summary>One test per row of VERIFICATION-MODEL.md section 6, EQ001-EQ006.</summary>
public sealed class VerdictRuleTests
{
    private static readonly RuntimeInterval Migration = new(TargetRuntime.Parse("net48")!, TargetRuntime.Parse("net10.0")!);

    [Fact]
    public void EquivalentIsEQ001()
    {
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Equivalent(ProofMethod.Bounded), Migration);
        Assert.Equal("EQ001", ruleId);
        Assert.Equal(FailureLevel.None, level);
        Assert.Equal(ResultKind.Pass, kind);
        Assert.Null(runtimeChange);
    }

    [Fact]
    public void DivergentIsEQ002()
    {
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Divergent(Fixtures.Counterexample()), Migration);
        Assert.Equal("EQ002", ruleId);
        Assert.Equal(FailureLevel.Error, level);
        Assert.Equal(ResultKind.Fail, kind);
        Assert.Null(runtimeChange);
    }

    [Fact]
    public void DivergentWithAFlaggedCallInTheTraceIsEQ006()
    {
        CallIdentity flagged = new("System.String::IndexOf(char)", RuntimeChanged: true);
        IrCallRecord record = new(flagged, [new IrBitVecValue(16, 'a')]);
        Counterexample counterexample = Fixtures.Counterexample() with { Old = Fixtures.Run() with { Trace = [record] } };

        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Divergent(counterexample), Migration);

        Assert.Equal("EQ006", ruleId);
        Assert.Equal(FailureLevel.Error, level);
        Assert.Equal(ResultKind.Fail, kind);
        Assert.NotNull(runtimeChange);
        Assert.StartsWith(runtimeChange.Member, flagged.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-055 (ADR 0040 decision 2): the row is looked up inside the pair's runtimes. <c>String.IndexOf</c> changed
    /// in .NET 5, so a pair that ends there has the row and one that starts there, or runs on one runtime, does not.
    /// </summary>
    [Theory]
    [InlineData("net48", "net5.0", "EQ006")]
    [InlineData("netcoreapp3.1", "net10.0", "EQ006")]
    [InlineData("net5.0", "net10.0", "EQ002")]
    [InlineData("net48", "netcoreapp3.1", "EQ002")]
    [InlineData("net10.0", "net10.0", "EQ002")]
    public void AFlaggedCallIsEQ006OnlyWhenItsRowLiesInsideThePairsRuntimes(string legacy, string modern, string expected)
    {
        IrCallRecord record = new(new CallIdentity("System.String::IndexOf(char)", RuntimeChanged: true), [new IrBitVecValue(16, 'a')]);
        Counterexample counterexample = Fixtures.Counterexample() with { New = Fixtures.Run() with { Trace = [record] } };

        (string ruleId, _, _, RuntimeChange? runtimeChange) =
            VerdictRule.Describe(new Divergent(counterexample), new RuntimeInterval(TargetRuntime.Parse(legacy)!, TargetRuntime.Parse(modern)!));

        Assert.Equal(expected, ruleId);
        Assert.Equal(string.Equals(expected, "EQ006", StringComparison.Ordinal), runtimeChange is not null);
    }

    [Fact]
    public void DivergentWithAnUnflaggedCallInTheTraceIsStillEQ002()
    {
        CallIdentity unflagged = new("System.String::Concat(string,string)");
        IrCallRecord record = new(unflagged, []);
        Counterexample counterexample = Fixtures.Counterexample() with { New = Fixtures.Run() with { Trace = [record] } };

        (string ruleId, _, _, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Divergent(counterexample), Migration);

        Assert.Equal("EQ002", ruleId);
        Assert.Null(runtimeChange);
    }

    [Fact]
    public void DivergentWithAFlaggedCallThatDoesNotMatchTheTableIsStillEQ002()
    {
        CallIdentity flaggedButUnmatched = new("Some.Unmatched.Type::Member()", RuntimeChanged: true);
        IrCallRecord record = new(flaggedButUnmatched, []);
        Counterexample counterexample = Fixtures.Counterexample() with { Old = Fixtures.Run() with { Trace = [record] } };

        (string ruleId, _, _, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Divergent(counterexample), Migration);

        Assert.Equal("EQ002", ruleId);
        Assert.Null(runtimeChange);
    }

    [Fact]
    public void UnknownIsEQ003()
    {
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Unknown(UnknownReason.Timeout, "gave up"), Migration);
        Assert.Equal("EQ003", ruleId);
        Assert.Equal(FailureLevel.Warning, level);
        Assert.Equal(ResultKind.Open, kind);
        Assert.Null(runtimeChange);
    }

    [Fact]
    public void AddedIsEQ004()
    {
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Added(), Migration);
        Assert.Equal("EQ004", ruleId);
        Assert.Equal(FailureLevel.Note, level);
        Assert.Equal(ResultKind.Informational, kind);
        Assert.Null(runtimeChange);
    }

    [Fact]
    public void RemovedIsEQ005()
    {
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Removed(), Migration);
        Assert.Equal("EQ005", ruleId);
        Assert.Equal(FailureLevel.Note, level);
        Assert.Equal(ResultKind.Informational, kind);
        Assert.Null(runtimeChange);
    }
}
