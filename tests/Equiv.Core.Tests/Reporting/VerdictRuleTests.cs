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
    [Fact]
    public void EquivalentIsEQ001()
    {
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Equivalent());
        Assert.Equal("EQ001", ruleId);
        Assert.Equal(FailureLevel.None, level);
        Assert.Equal(ResultKind.Pass, kind);
        Assert.Null(runtimeChange);
    }

    [Fact]
    public void DivergentIsEQ002()
    {
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Divergent(Fixtures.Counterexample()));
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

        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Divergent(counterexample));

        Assert.Equal("EQ006", ruleId);
        Assert.Equal(FailureLevel.Error, level);
        Assert.Equal(ResultKind.Fail, kind);
        Assert.NotNull(runtimeChange);
        Assert.StartsWith(runtimeChange.Member, flagged.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void DivergentWithAnUnflaggedCallInTheTraceIsStillEQ002()
    {
        CallIdentity unflagged = new("System.String::Concat(string,string)");
        IrCallRecord record = new(unflagged, []);
        Counterexample counterexample = Fixtures.Counterexample() with { New = Fixtures.Run() with { Trace = [record] } };

        (string ruleId, _, _, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Divergent(counterexample));

        Assert.Equal("EQ002", ruleId);
        Assert.Null(runtimeChange);
    }

    [Fact]
    public void UnknownIsEQ003()
    {
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Unknown(UnknownReason.Timeout, "gave up"));
        Assert.Equal("EQ003", ruleId);
        Assert.Equal(FailureLevel.Warning, level);
        Assert.Equal(ResultKind.Open, kind);
        Assert.Null(runtimeChange);
    }

    [Fact]
    public void AddedIsEQ004()
    {
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Added());
        Assert.Equal("EQ004", ruleId);
        Assert.Equal(FailureLevel.Note, level);
        Assert.Equal(ResultKind.Informational, kind);
        Assert.Null(runtimeChange);
    }

    [Fact]
    public void RemovedIsEQ005()
    {
        (string ruleId, FailureLevel level, ResultKind kind, RuntimeChange? runtimeChange) = VerdictRule.Describe(new Removed());
        Assert.Equal("EQ005", ruleId);
        Assert.Equal(FailureLevel.Note, level);
        Assert.Equal(ResultKind.Informational, kind);
        Assert.Null(runtimeChange);
    }
}
