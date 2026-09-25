using System.Text;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using VerifyXunit;

using Xunit;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// One small fixture per instruction and terminator kind, <c>Fixtures/kinds/*.ir</c> (ticket M3-001 deliverable 2): the encoder's
/// assertions, rendered with <c>Expr.ToString()</c>, are pinned by a snapshot, and the pair gets the
/// verdict its first line names.
/// </summary>
public sealed class EncoderSnapshotTests
{
    public static TheoryData<string> Kinds =>
    [
        "const", "binary", "overflows", "unary", "phi-branch", "switch", "branch-same-target", "call", "call-heap", "map", "literals", "opaque", "throw", "unreachable",
    ];

    [Theory]
    [MemberData(nameof(Kinds))]
    public Task EncodingIsPinnedAndVerdictHolds(string name)
    {
        Fixture fixture = Fixture.Load("kinds/" + name);

        Verdict verdict = new Z3Backend().Verify(fixture.Old, fixture.New, new VerificationOptions(3, 10_000, []));

        Assert.Equal(fixture.Expected, verdict is Unknown unknown ? $"Unknown({unknown.Reason})" : verdict.GetType().Name);
        return Verifier.Verify(Dump(fixture.Old, fixture.New)).UseParameters(name);
    }

    [Fact]
    public void BlocksNoInputReachesAreNotEncoded()
    {
        (IrProcedure old, _) = Fixture.Pair("""
            proc "T::M()" () entry B0
            B0:
              ret
            B1:
              %x: bv32 = const bv32 1
              goto B1
            ---
            proc "T::M()" () entry B0
            B0:
              ret
            """);

        Assert.Equal([new IrBlockId(0)], IrLoopAnalysis.Of(old).ReversePostorder.Select(static b => b.Id));
        Assert.DoesNotContain("old.x", Dump(old, old), StringComparison.Ordinal);
    }

    private static string Dump(IrProcedure old, IrProcedure @new)
    {
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, old, @new, []);
        StringBuilder text = new();
        foreach (BoolExpr assertion in encoding.Assertions)
        {
            text.Append(assertion).Append('\n');
        }

        return text.Append("differs: ").Append(encoding.Differs).Append('\n')
            .Append("opaque.old: ").Append(encoding.OpaqueOld).Append('\n')
            .Append("opaque.new: ").Append(encoding.OpaqueNew).Append('\n')
            .Append("unreachable.old: ").Append(encoding.Old.Unreachable).Append('\n')
            .Append("unreachable.new: ").Append(encoding.New.Unreachable).Append('\n')
            .ToString();
    }
}
