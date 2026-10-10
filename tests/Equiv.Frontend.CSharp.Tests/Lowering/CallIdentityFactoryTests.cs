using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.RuntimeChanges;
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

    /// <summary>
    /// Ticket P2-042: a nullable reference annotation on a type argument, its array element or a nested type argument
    /// is not part of the identity, so a legacy call and its annotated modern form are one function.
    /// </summary>
    [Theory]
    [InlineData("string", "string?", "System.Linq.Enumerable::Empty`1()<string>")]
    [InlineData("string[]", "string?[]?", "System.Linq.Enumerable::Empty`1()<string[]>")]
    [InlineData("System.Collections.Generic.List<string>", "System.Collections.Generic.List<string?>?", "System.Linq.Enumerable::Empty`1()<System.Collections.Generic.List<string>>")]
    public void ANullableAnnotationOnATypeArgumentIsNotPartOfTheIdentity(string legacy, string modern, string identity)
    {
        Assert.Equal(identity, Assert.Single(Calls(Method($"static object M() => System.Linq.Enumerable.Empty<{legacy}>();"))).Callee.Value);
        Assert.Equal(identity, Assert.Single(Calls(Source(
            $"#nullable enable\nclass C {{ static object M() => System.Linq.Enumerable.Empty<{modern}>(); }}"))).Callee.Value);
    }

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

    /// <summary>Ticket P2-055 (ADR 0040 decision 2): on a same-runtime pair no row applies, so no callee is runtime-changed.</summary>
    [Fact]
    public void SameRuntime_NothingIsRuntimeChanged()
    {
        const string Source = "class C { static int M(string s, char c) => s.IndexOf(c); }";
        RuntimeInterval same = Runtimes.Interval("net10.0", "net10.0");

        Assert.True(Assert.Single(Calls(Lowered.Source(Source))).Callee.RuntimeChanged);
        Assert.False(Assert.Single(Calls(Lowered.Source(Source, runtime: Runtimes.Between("net10.0", "net10.0")))).Callee.RuntimeChanged);
        Assert.All(RuntimeChangeTable.Load().Rows, row => Assert.False(CallIdentityFactory.Of(row.Member + "x()", [], same).RuntimeChanged, row.Member));
    }

    /// <summary>
    /// Ticket P2-055: a row applies only where the pair crosses its change point. <c>String.IndexOf</c> changed in .NET 5,
    /// <c>Double.ToString</c> in .NET Core 3.0 and <c>BinaryReader.ReadString</c> in .NET 9.
    /// </summary>
    [Theory]
    [InlineData("static int M(string s, char c) => s.IndexOf(c);", "net48", "net10.0", true)]
    [InlineData("static int M(string s, char c) => s.IndexOf(c);", "netcoreapp3.1", "net5.0", true)]
    [InlineData("static int M(string s, char c) => s.IndexOf(c);", "net8.0", "net10.0", false)]
    [InlineData("static int M(string s, char c) => s.IndexOf(c);", "net48", "netcoreapp3.1", false)]
    [InlineData("static string M(double d) => d.ToString();", "net48", "net10.0", true)]
    [InlineData("static string M(double d) => d.ToString();", "net8.0", "net10.0", false)]
    [InlineData("static string M(System.IO.BinaryReader r) => r.ReadString();", "net8.0", "net10.0", true)]
    [InlineData("static string M(System.IO.BinaryReader r) => r.ReadString();", "net10.0", "net8.0", true)]
    [InlineData("static string M(System.IO.BinaryReader r) => r.ReadString();", "net9.0", "net10.0", false)]
    public void RowOutsideTheInterval_DoesNotApply(string member, string legacy, string modern, bool expected)
    {
        IrProcedure procedure = Lowered.Source($"class C {{ {member} }}", runtime: Runtimes.Between(legacy, modern));

        Assert.Equal(expected, Assert.Single(Calls(procedure)).Callee.RuntimeChanged);
    }

    /// <summary>
    /// Ticket P2-075: a culture-comparison row does not flag a call that passes <c>StringComparison.Ordinal</c> or
    /// <c>OrdinalIgnoreCase</c> as a constant. A culture comparison, one that is not a constant, a call with no comparison and a
    /// constant that is not a <c>StringComparison</c> but has the same value stay flagged.
    /// </summary>
    [Theory]
    [InlineData("static int M(string s) => s.IndexOf(\"x\", System.StringComparison.Ordinal);", false)]
    [InlineData("static int M(string s) => s.IndexOf(\"x\", System.StringComparison.OrdinalIgnoreCase);", false)]
    [InlineData("static int M(string s) => s.IndexOf(\"x\", 1, System.StringComparison.Ordinal);", false)]
    [InlineData("static int M(string s) => s.LastIndexOf(\"x\", System.StringComparison.OrdinalIgnoreCase);", false)]
    [InlineData("static bool M(string s) => s.StartsWith(\"x\", System.StringComparison.Ordinal);", false)]
    [InlineData("static bool M(string s) => s.EndsWith(\"x\", System.StringComparison.OrdinalIgnoreCase);", false)]
    [InlineData("static int M(string a, string b) => string.Compare(a, b, System.StringComparison.Ordinal);", false)]
    [InlineData("static bool M(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);", false)]
    [InlineData("static bool M(string a, string b) => a.Equals(b, System.StringComparison.Ordinal);", false)]
    [InlineData("static int M(string s) => s.IndexOf(\"x\", System.StringComparison.CurrentCulture);", true)]
    [InlineData("static int M(string s) => s.IndexOf(\"x\", System.StringComparison.CurrentCultureIgnoreCase);", true)]
    [InlineData("static int M(string s) => s.IndexOf(\"x\", System.StringComparison.InvariantCulture);", true)]
    [InlineData("static int M(string s, System.StringComparison c) => s.IndexOf(\"x\", c);", true)]
    [InlineData("static int M(string s) => s.IndexOf(\"x\");", true)]
    [InlineData("static int M(string s) => s.IndexOf('x', 4);", true)]
    [InlineData("static int M(string s) => s.IndexOf('x', 5);", true)]
    [InlineData("static int M(string s) => s.GetHashCode();", true)]
    public void AnOrdinalComparisonConstantIsNotRuntimeChanged(string member, bool expected) =>
        Assert.Equal(expected, Assert.Single(Calls(Method(member))).Callee.RuntimeChanged);

    /// <summary>An <c>await</c>'s identity and an API equivalence's modern member are flagged inside the same interval as any callee.</summary>
    [Fact]
    public void EveryIdentityIsFlaggedInsideTheInterval()
    {
        Compilation compilation = RoslynTestCompilations.Compile("class C { }");
        INamedTypeSymbol awaiter = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.TaskAwaiter")!;
        ImmutableArray<string> suppressed = ["await:System.Runtime.CompilerServices.TaskAwaiter"];

        Assert.False(CallIdentityFactory.Await(awaiter, RenameMap.Empty, [], Runtimes.Interval("net48", "net10.0")).RuntimeChanged);
        Assert.Equal("await:System.Runtime.CompilerServices.TaskAwaiter", CallIdentityFactory.Await(awaiter, RenameMap.Empty, suppressed, Runtimes.Interval("net48", "net10.0")).Value);
        Assert.True(CallIdentityFactory.Of("System.String::IndexOf(char)", [], Runtimes.Interval("net48", "net10.0")).RuntimeChanged);
        Assert.False(CallIdentityFactory.Of("System.String::IndexOf(char)", ["System.String::IndexOf("], Runtimes.Interval("net48", "net10.0")).RuntimeChanged);
        Assert.False(CallIdentityFactory.Of("System.String::IndexOf(char)", [], Runtimes.Interval("net6.0", "net10.0")).RuntimeChanged);
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
        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration);
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
