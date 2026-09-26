using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

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
    public void ASuppressedMemberIsNotMarkedRuntimeChanged()
    {
        const string Source = "class C { static int M(string s, char c) => s.IndexOf(c); }";

        Assert.True(Assert.Single(Calls(Lowered.Source(Source))).Callee.RuntimeChanged);
        Assert.False(Assert.Single(Calls(Lowered.Source(Source, suppressedRuntimeChanges: ["System.String::IndexOf("]))).Callee.RuntimeChanged);
    }

    [Fact]
    public void AnUnflaggedCallIsNotMarkedRuntimeChanged()
    {
        IrProcedure procedure = Method("static int M(int a) => System.Math.Abs(a);");
        Assert.False(Assert.Single(Calls(procedure)).Callee.RuntimeChanged);
    }

    /// <summary>Ticket M3-033: a call to a member of a reference assembly (the framework or a .NET reference pack) is external.</summary>
    [Fact]
    public void ACallToAReferenceAssemblyMemberIsExternal()
    {
        MetadataReference pack = Library("Pack", isReferenceAssembly: true, "public static class R { public static int F(int a) => a; }");
        IrProcedure procedure = Lower("class C { static int M(int a) => R.F(a); }", pack);

        Assert.True(Assert.Single(Calls(procedure)).Callee.External);
    }

    /// <summary>A call within the same assembly (the solution's own code) is never external, even to a BCL-looking helper.</summary>
    [Fact]
    public void ACallWithinTheSameAssemblyIsNotExternal()
    {
        IrProcedure procedure = Method("static int F(int a) => a; static int M(int a) => F(a);");
        Assert.False(Assert.Single(Calls(procedure)).Callee.External);
    }

    /// <summary>A call to another project of the same solution (a compilation reference) is not external.</summary>
    [Fact]
    public void ACallToAnotherProjectOfTheSolutionIsNotExternal()
    {
        Compilation other = RoslynTestCompilations.Compile("public static class O { public static int F(int a) => a; }", [], "Other");
        IrProcedure procedure = Lower("class C { static int M(int a) => O.F(a); }", other.ToReference());

        Assert.False(Assert.Single(Calls(procedure)).Callee.External);
    }

    /// <summary>A call to a plain metadata reference without <c>ReferenceAssemblyAttribute</c> (a NuGet package) is not external.</summary>
    [Fact]
    public void ACallToAPackageMemberIsNotExternal()
    {
        MetadataReference package = Library("Package", isReferenceAssembly: false, "public static class P { public static int F(int a) => a; }");
        IrProcedure procedure = Lower("class C { static int M(int a) => P.F(a); }", package);

        Assert.False(Assert.Single(Calls(procedure)).Callee.External);
    }

    private static IrProcedure Lower(string source, MetadataReference extraReference)
    {
        Compilation compilation = RoslynTestCompilations.Compile(source, [extraReference]);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, []);
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    private static PortableExecutableReference Library(string name, bool isReferenceAssembly, string source)
    {
        string attribute = isReferenceAssembly ? "[assembly: System.Runtime.CompilerServices.ReferenceAssembly]\n" : string.Empty;
        Compilation compilation = RoslynTestCompilations.Compile(attribute + source, [], name);
        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }
}
