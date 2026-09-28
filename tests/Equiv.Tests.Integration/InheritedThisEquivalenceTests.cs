using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-031: a member declared on a base class, reached through a derived class's <c>this</c> or a derived-typed
/// value, verifies instead of Z3 rejecting the derived sort where the base sort is expected.
/// </summary>
[Trait("Category", "Integration")]
public sealed class InheritedThisEquivalenceTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    [Theory]
    [InlineData("class Base { public virtual int F(Base b) => 1; } class C : Base { public override int F(Base b) => 2; public int M() => F(this); }")]
    [InlineData("class Base { public int F() => 1; } class C : Base { public int M() => F(); }")]
    [InlineData("class Base { public int X; } class C : Base { public int M() => X + 1; }")]
    [InlineData("class Base { public int X; } class C : Base { public int M() { X = 3; return X; } }")]
    [InlineData("class Base { public int P { get; set; } } class C : Base { public int M() => P; }")]
    [InlineData("class Base { public int X; } class C : Base { public static int M(C c) => c.X; }")]
    [InlineData("class Base { public int F() => 1; } class C : Base { public static int M(C c) => c.F(); }")]
    [InlineData("class Base { public virtual int F() => 1; } class C : Base { public override int F() => base.F() + 1; public int M() => base.F(); }")]
    [InlineData("class Base { public int X; } class C : Base { public int M() => base.X; }")]
    [InlineData("class Base { public int X; } class C : Base { public int M() { base.X = 3; return base.X; } }")]
    public void AMemberOfABaseClassReachedFromADerivedOneVerifies(string source) =>
        Assert.IsType<Equivalent>(Verify(source, source));

    /// <summary>
    /// The <c>pmb-shiningrush__serviceant</c> pair's <c>GenericRequest_ShouldResponse</c> shape: a generic class's
    /// auto-property read through a generic class derived from it, which Z3 rejected as <c>domain sort |TestEventDataT`1|
    /// and parameter |TransportTray`1| do not match</c>.
    /// </summary>
    [Fact]
    public void AGenericBaseAutoPropertyReadThroughAGenericDerivedReceiverVerifies()
    {
        const string source = """
            class TransportTray { }
            class TransportTray<TEntity> : TransportTray
            {
                public TransportTray(TEntity entity) { TransportEntity = entity; }
                public TEntity TransportEntity { get; set; }
            }
            class C
            {
                private class TestEventData : TransportTray { public string Msg { get; set; } }
                private class TestEventDataT<T> : TransportTray<T>
                {
                    public TestEventDataT(T test) : base(test) { }
                    public string Msg { get; set; }
                }
                public string M()
                {
                    var data = new TestEventDataT<TestEventData>(new TestEventData() { Msg = "non" }) { Msg = "success" };
                    return data.TransportEntity.Msg + data.Msg;
                }
            }
            """;
        Assert.IsType<Equivalent>(Verify(source, source));
    }

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower(legacy, isLegacy: true), Lower(modern, isLegacy: false), Options);

    private static IrProcedure Lower(string source, bool isLegacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText("using System;\n" + source, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
