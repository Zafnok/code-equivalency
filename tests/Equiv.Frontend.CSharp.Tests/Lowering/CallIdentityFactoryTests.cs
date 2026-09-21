using Equiv.Core.Configuration;
using Equiv.Core.Ir;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;
using static VerifyXunit.Verifier;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>Opaque-call identities: the M2-002 identity string, plus type arguments for constructed generics.</summary>
public sealed class CallIdentityFactoryTests
{
    [Theory]
    [InlineData("static int M(int a) => Math.Abs(a);", "System.Math::Abs(int)")]
    [InlineData("static int[] M() => Array.Empty<int>();", "System.Array::Empty`1()<int>")]
    [InlineData("static long[] M() => Array.Empty<long>();", "System.Array::Empty`1()<long>")]
    [InlineData("static int M(int? n) => n.GetValueOrDefault();", "System.Nullable`1::GetValueOrDefault()<int>")]
    [InlineData("static bool M(System.Collections.Generic.List<int>.Enumerator e) => e.MoveNext();", "System.Collections.Generic.List`1.Enumerator::MoveNext()<int>")]
    public void IdentityNamesTheCalleeAndItsTypeArguments(string members, string identity) =>
        Assert.Equal(identity, Assert.Single(Calls(Method(members))).Callee.Value);

    [Fact]
    public void RenameMapApplies()
    {
        RenameMap renames = RenameMap.Empty with { Namespaces = RenameMap.Empty.Namespaces.Add("Old", "New") };
        IrProcedure procedure = Source(
            "namespace Old { static class H { public static int F(int a) => a; } }\nclass C { static int M(int a) => Old.H.F(a); }",
            renames: renames);

        Assert.Equal("New.H::F(int)", Assert.Single(Calls(procedure)).Callee.Value);
        Assert.Equal("C::M(int)", procedure.Identity.Value);
    }

    /// <summary>Ticket M2-006: a call to a runtime-changes-table member is flagged, dumped with a <c>!</c> suffix.</summary>
    [Fact]
    public Task Lowering_FlagsIndexOfCall() => Verify(IrText.Dump(Method("static int M(string s, char c) => s.IndexOf(c);")));

    [Fact]
    public void AnUnflaggedCallIsNotMarkedRuntimeChanged()
    {
        IrProcedure procedure = Method("static int M(int a) => System.Math.Abs(a);");
        Assert.False(Assert.Single(Calls(procedure)).Callee.RuntimeChanged);
    }
}
