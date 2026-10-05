using Equiv.Core;

using Xunit;

namespace Equiv.Verify.Cvc5.Tests;

/// <summary>Ticket P1-033: the answer parser. Values are read by name, never by position.</summary>
public sealed class SmtOutputTests
{
    [Theory]
    [InlineData("unsat")]
    [InlineData("unsat\n")]
    [InlineData("\r\n  unsat  \r\n")]
    public void Parse_Unsat(string output)
    {
        Assert.IsType<SmtUnsat>(SmtOutput.Parse(output));
    }

    [Fact]
    public void Parse_SatReadsEachValueByItsName()
    {
        SmtSat sat = Assert.IsType<SmtSat>(SmtOutput.Parse(
            "sat\r\n((x #b0101) (|in.a b|  #xff)\n (flag true)(|odd)(name| false) (wide (_ bv5 32)) (neg (- 1)))\n"));

        Assert.Equal(
            [("flag", "true"), ("neg", "(- 1)"), ("wide", "(_ bv5 32)"), ("x", "#b0101"), ("|in.a b|", "#xff"), ("|odd)(name|", "false")],
            sat.Values.OrderBy(static v => v.Key, StringComparer.Ordinal).Select(static v => (v.Key, v.Value)));
    }

    [Fact]
    public void Parse_SatWithNoValuesHasNone()
    {
        Assert.Empty(Assert.IsType<SmtSat>(SmtOutput.Parse("sat")).Values);
        Assert.Empty(Assert.IsType<SmtSat>(SmtOutput.Parse("sat\n(error \"no model\")\n")).Values);
    }

    [Fact]
    public void Parse_ARepeatedNameKeepsItsLastValue()
    {
        SmtSat sat = Assert.IsType<SmtSat>(SmtOutput.Parse("sat\n((x true) (x false))"));

        Assert.Equal("false", Assert.Single(sat.Values).Value);
    }

    [Theory]
    [InlineData("unknown\n(:reason-unknown resourceout)", "unknown")]
    [InlineData("cvc5 interrupted by timeout.\n", "cvc5 interrupted by timeout.")]
    [InlineData("(error \"Parse Error: q.smt2:3.7: expected a value\")\nmore", "(error \"Parse Error: q.smt2:3.7: expected a value\")")]
    [InlineData("", "")]
    [InlineData("satisfiable", "satisfiable")]
    [InlineData("unsatisfiable", "unsatisfiable")]
    public void Parse_AnythingElseIsUnknownWithTheFirstLineAsItsReason(string output, string reason)
    {
        Assert.Equal(reason, Assert.IsType<SmtUnknown>(SmtOutput.Parse(output)).Reason);
    }

    [Fact]
    public void Parse_ALongReasonIsCut()
    {
        Assert.Equal(new string('e', 200), Assert.IsType<SmtUnknown>(SmtOutput.Parse(new string('e', 201))).Reason);
        Assert.Equal(new string('e', 200), Assert.IsType<SmtUnknown>(SmtOutput.Parse(new string('e', 200))).Reason);
    }

    [Theory]
    [InlineData("cvc5 1.4.1 [git 2b2e844 on branch HEAD]\ncompiled as a unrestricted build with GCC version Clang 22.1.7\n", "1.4.1")]
    [InlineData("This is cvc5 version 1.0.0 [git tag 1.0.0 branch HEAD]\n", "1.0.0")]
    [InlineData("cvc5   2.0", "2.0")]
    [InlineData("compiled with GCC version Clang 22.1.7", "unknown")]
    [InlineData("cvc5 version unreleased", "unknown")]
    [InlineData("z3 4.15", "unknown")]
    [InlineData(null, "unknown")]
    public void Version_IsTheNumberAfterTheName(string? output, string version)
    {
        Assert.Equal(version, SmtOutput.Version(output));
    }
}
