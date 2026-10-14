using Xunit;

namespace Equiv.Verify.Cvc5.Tests;

/// <summary>Ticket P1-039: the partition file cvc5 writes, a partition's script, and the cover check's script.</summary>
public sealed class SmtPartitionsTests
{
    private const string Script = """
        (set-option :produce-models true)
        (set-logic QF_UFBV)
        (declare-fun |a(b| () (_ BitVec 8))
        (declare-datatypes ((Pair 0)) (((mk (fst Bool)))))
        (define-fun two () (_ BitVec 8) #x02)
        (assert (= |a(b| two))
        (assert (distinct "x)" "y"))
        (check-sat)
        (get-value (|a(b|))

        """;

    [Fact]
    public void Read_KeepsEachPartitionOnceInTheOrderWritten()
    {
        Assert.Equal(["(= a b)", "(not (= a b))", "c"], SmtPartitions.Read("(= a b)\n  (not (= a b)) \r\n\n(= a b)\nc\n(not (= a b))"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n \r\n")]
    public void Read_AnEmptyFileHasNoPartition(string text)
    {
        Assert.Empty(SmtPartitions.Read(text));
    }

    [Fact]
    public void With_AssertsThePartitionBeforeTheLastCheckSat()
    {
        Assert.Equal("(check-sat)\n(assert x)\n(assert p)\n(check-sat)\n(get-value (x))\n", SmtPartitions.With("(check-sat)\n(assert x)\n(check-sat)\n(get-value (x))\n", "p"));
    }

    /// <summary>The cover keeps the logic and every declaration and definition, and nothing the query asserts or asks.</summary>
    [Fact]
    public void Cover_IsTheDeclarationsAndTheNegationOfThePartitionsDisjunction()
    {
        Assert.Equal(
            """
            (set-logic QF_UFBV)
            (declare-fun |a(b| () (_ BitVec 8))
            (declare-datatypes ((Pair 0)) (((mk (fst Bool)))))
            (define-fun two () (_ BitVec 8) #x02)
            (assert (not (or false (= |a(b| two) (not (= |a(b| two)))))
            (check-sat)

            """.ReplaceLineEndings("\n"),
            SmtPartitions.Cover(Script.ReplaceLineEndings("\n"), ["(= |a(b| two)", "(not (= |a(b| two))"]));
    }

    [Theory]
    [InlineData("(declare-fun x () Bool)\n(assert (= |x")]
    [InlineData("(declare-fun x () Bool)\n(assert (= \"x")]
    public void Cover_AnUnclosedSymbolOrStringEndsTheScript(string script)
    {
        Assert.Equal("(declare-fun x () Bool)\n(assert (not (or false p)))\n(check-sat)\n", SmtPartitions.Cover(script, ["p"]));
    }
}
