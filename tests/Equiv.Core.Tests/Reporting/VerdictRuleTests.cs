using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Core.Tests.Reporting;

/// <summary>One test per row of VERIFICATION-MODEL.md section 6, EQ001-EQ005.</summary>
public sealed class VerdictRuleTests
{
    [Fact]
    public void EquivalentIsEQ001()
    {
        (string ruleId, FailureLevel level, ResultKind kind) = VerdictRule.Describe(new Equivalent());
        Assert.Equal("EQ001", ruleId);
        Assert.Equal(FailureLevel.None, level);
        Assert.Equal(ResultKind.Pass, kind);
    }

    [Fact]
    public void DivergentIsEQ002()
    {
        (string ruleId, FailureLevel level, ResultKind kind) = VerdictRule.Describe(new Divergent(Fixtures.Counterexample()));
        Assert.Equal("EQ002", ruleId);
        Assert.Equal(FailureLevel.Error, level);
        Assert.Equal(ResultKind.Fail, kind);
    }

    [Fact]
    public void UnknownIsEQ003()
    {
        (string ruleId, FailureLevel level, ResultKind kind) = VerdictRule.Describe(new Unknown(UnknownReason.Timeout, "gave up"));
        Assert.Equal("EQ003", ruleId);
        Assert.Equal(FailureLevel.Warning, level);
        Assert.Equal(ResultKind.Open, kind);
    }

    [Fact]
    public void AddedIsEQ004()
    {
        (string ruleId, FailureLevel level, ResultKind kind) = VerdictRule.Describe(new Added());
        Assert.Equal("EQ004", ruleId);
        Assert.Equal(FailureLevel.Note, level);
        Assert.Equal(ResultKind.Informational, kind);
    }

    [Fact]
    public void RemovedIsEQ005()
    {
        (string ruleId, FailureLevel level, ResultKind kind) = VerdictRule.Describe(new Removed());
        Assert.Equal("EQ005", ruleId);
        Assert.Equal(FailureLevel.Note, level);
        Assert.Equal(ResultKind.Informational, kind);
    }
}
