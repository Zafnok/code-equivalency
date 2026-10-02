using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-085 (ADR 0029 decision 2 as clarified): which methods of a project that does not compile are one
/// <c>unbound</c> opaque, and which are lowered as they are bound. Lines are 1-based, as a <see cref="SourceSpan"/> is.
/// </summary>
public sealed class UnboundCodeTests
{
    /// <summary>One <c>;</c> is missing in <c>A</c>; <c>B</c> and <c>M</c> hold no diagnostic of their own.</summary>
    private const string MissingSemicolon = "class C {\n int A(int x) { int y = x\n return y; }\n int B(int y) { return y; }\n int M => 1;\n}";

    /// <summary>One <c>}</c> too many after <c>A</c>: <c>A</c> holds no diagnostic, and <c>B</c> is parsed as a top-level statement.</summary>
    private const string ExtraBrace = "class C {\n int A(int x) { return 1; } }\n public int B(int y) { return y; }\n}";

    /// <summary>
    /// Acceptance criterion 3, one case per error kind: a method with an error diagnostic inside its declaration is one
    /// <c>unbound</c> opaque, which the CLI never calls Equivalent (<c>CompareCommandTests.AnUnboundMethodIsNeverCongruent</c>).
    /// </summary>
    [Theory]
    [InlineData("CS0246", "static int M(int a) { Missing m = null; return a; }")] // type or namespace not found
    [InlineData("CS0234", "static int M(int a) { System.Missing.Thing t = null; return a; }")] // not in the namespace
    [InlineData("CS0400", "static int M(int a) { global::Missing m = null; return a; }")] // not in the global namespace
    [InlineData("CS0103", "static int M(int a) { return a + Undefined(); }")] // name does not exist
    [InlineData("CS1061", "static int M(string s) { return s.Missing(); }")] // member does not exist
    [InlineData("CS0246", "static int M(Missing m) { return 1; }")] // in the signature
    [InlineData("CS0246", "[Missing] static int M(int a) { return a; }")] // in an attribute
    [InlineData("CS1002", "static int M(int a) { return a }")] // ; expected
    [InlineData("CS1513", "static int M(int a) { if (a > 0) { return a; }")] // } expected
    public void AMethodWithAnErrorInsideItsDeclarationIsUnbound(string id, string member)
    {
        Assert.Contains(
            RoslynTestCompilations.Compile($"using System;\nclass C\n{{\n{member}\n}}\n").GetDiagnostics(TestContext.Current.CancellationToken),
            d => d.Severity == DiagnosticSeverity.Error && string.Equals(d.Id, id, StringComparison.Ordinal));

        ImmutableArray<IrOpaque> opaques = Opaques(Method(member, allowErrors: true));

        Assert.NotEmpty(opaques);
        Assert.All(opaques, static o => Assert.Equal(("unbound", true), (o.Reason, o.WholeBody)));
    }

    /// <summary>
    /// CS0012: the method uses a type whose base is defined in an assembly the project does not reference. The reference
    /// that is there resolves, so on the modern side the project is kept and only this method is unbound.
    /// </summary>
    [Fact]
    public void AMethodThatNeedsATypeFromAnUnreferencedAssemblyIsUnbound()
    {
        Compilation basis = RoslynTestCompilations.Compile("public class Base { }", "Basis");
        Compilation middle = RoslynTestCompilations.Compile("public class Mid : Base { public int Value() { return 1; } }", [basis.ToReference()], "Middle");
        using MemoryStream image = new();
        Assert.True(middle.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
        Compilation compilation = RoslynTestCompilations.Compile(
            "class C {\n static int M(Mid m) { return m.Value(); }\n static int Clean(int a) { return a; }\n}",
            [MetadataReference.CreateFromImage(image.ToArray())]);
        Assert.Contains(compilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Id is "CS0012");
        INamedTypeSymbol type = compilation.GetTypeByMetadataName("C")!;

        IrOpaque opaque = Assert.Single(Opaques(IrLowerer.Lower(type.GetMembers("M").OfType<IMethodSymbol>().Single(), compilation, RenameMap.Empty, [])));

        Assert.Equal("unbound", opaque.Reason);
        Assert.Empty(Opaques(IrLowerer.Lower(type.GetMembers("Clean").OfType<IMethodSymbol>().Single(), compilation, RenameMap.Empty, [])));
    }

    [Theory]
    [InlineData(MissingSemicolon, "A", 2, 26)]
    [InlineData(MissingSemicolon, "B", 2, 26)]
    [InlineData(MissingSemicolon, "get_M", 2, 26)]
    [InlineData(ExtraBrace, "A", 3, 2)]
    public void EveryMethodInAFileWithASyntaxErrorIsUnboundAtItsFirstSyntaxError(string source, string method, int line, int column)
    {
        IrOpaque opaque = Assert.Single(Opaques(Source(source, method, allowErrors: true)));

        Assert.Equal("unbound", opaque.Reason);
        Assert.True(opaque.WholeBody);
        Assert.Equal((line, column), (opaque.Span.StartLine, opaque.Span.StartColumn));
    }

    /// <summary>Only the first syntax error is a cause: the later ones are often the parser recovering from the first.</summary>
    [Fact]
    public void TheFirstSyntaxErrorIsTheOnlyCause()
    {
        IrProcedure procedure = Source("class C {\n int M(int x) { int y = x\n int z = y\n return z; }\n}", allowErrors: true);

        IrOpaque opaque = Assert.Single(Opaques(procedure));
        Assert.Equal((2, 26), (opaque.Span.StartLine, opaque.Span.StartColumn));
    }

    /// <summary>A constructor's initializer in another file of a partial type has that file's syntax errors.</summary>
    [Fact]
    public void ASyntaxErrorInAnInitializersFileMakesTheConstructorUnbound()
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [
                CSharpSyntaxTree.ParseText("partial class C {\n C() { }\n}\n", path: "Snippet.cs", cancellationToken: TestContext.Current.CancellationToken),
                CSharpSyntaxTree.ParseText("partial class C {\n int f = 1;\n int Broken() { return 1 }\n}", path: "More.cs", cancellationToken: TestContext.Current.CancellationToken),
            ],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        IMethodSymbol constructor = compilation.GetTypeByMetadataName("C")!.InstanceConstructors.Single();

        IrOpaque opaque = Assert.Single(Opaques(IrLowerer.Lower(constructor, compilation, RenameMap.Empty, [])));

        Assert.Equal("unbound", opaque.Reason);
        Assert.Equal(3, opaque.Span.StartLine);
        Assert.EndsWith("More.cs", opaque.Span.Path, StringComparison.Ordinal);
    }

    /// <summary>Binding errors are causes in source order, so the first cause is the first error a reader meets.</summary>
    [Fact]
    public void BindingErrorsAreCausesInSourceOrder()
    {
        // CS0161 (not all code paths return a value) is on M's name, line 4; the binder reports the body's CS0103 first.
        IrProcedure procedure = Method("static int M(int a) {\n Undefined();\n }", allowErrors: true);

        Assert.Equal([4, 5], Opaques(procedure).Select(static o => o.Span.StartLine));
    }

    /// <summary>
    /// The property's type did not resolve. The diagnostic is on the property, outside the accessor, and an accessor with
    /// no body has no operation to hold an error type.
    /// </summary>
    [Theory]
    [InlineData("public Missing P { get; set; }", "get_P")]
    [InlineData("public Missing P { get; set; }", "set_P")]
    [InlineData("public System.Collections.Generic.List<Missing> P { get; }", "get_P")]
    [InlineData("public System.Collections.Generic.Dictionary<int, Missing[]> P { get; }", "get_P")]
    [InlineData("public Missing[] P { get; }", "get_P")]
    [InlineData("public int this[Missing key] { get { return 1; } }", "get_Item")]
    public void AnAccessorWithAnErrorTypeInItsSignatureIsUnbound(string member, string accessor)
    {
        IrOpaque opaque = Assert.Single(Opaques(Method(member, accessor, allowErrors: true)));

        Assert.Equal("unbound", opaque.Reason);
        Assert.True(opaque.WholeBody);
        Assert.Equal(4, opaque.Span.StartLine);
    }

    /// <summary>The other accessors of a type with an unresolved member are lowered as they are bound.</summary>
    [Theory]
    [InlineData("get_Q")]
    [InlineData("set_Q")]
    [InlineData("get_R")]
    public void AnAccessorWhoseSignatureResolvesIsLowered(string accessor) =>
        Assert.Empty(Opaques(Method("public Missing P { get; set; }\npublic int Q { get; set; }\npublic System.Collections.Generic.List<int[]> R { get; }", accessor, allowErrors: true)));

    /// <summary>A type parameter in a signature is not an error type, in a project with errors or without.</summary>
    [Fact]
    public void AMethodOverATypeParameterIsLowered() =>
        Assert.DoesNotContain(Opaques(Method("Missing f;\nstatic T M<T>(T x) { return x; }", allowErrors: true)), static o => o.Reason is "unbound");

    /// <summary>
    /// The base type did not resolve, so the constructor's bound body leaves the base constructor call out without a
    /// diagnostic in the constructor: lowered as bound, it would differ from the legacy constructor by that call.
    /// </summary>
    [Theory]
    [InlineData("public C(int x) { f = x; }", 1)]
    [InlineData("public C() : this(1) { } public C(int x) { f = x; }", 0)]
    public void AConstructorOfATypeWhoseBaseDidNotResolveIsUnbound(string constructors, int parameters)
    {
        IrProcedure procedure = Source($"class C : Missing {{\n int f;\n {constructors}\n}}", ".ctor", allowErrors: true, parameters: parameters);

        IrOpaque opaque = Assert.Single(Opaques(procedure));
        Assert.Equal("unbound", opaque.Reason);
        Assert.Equal(3, opaque.Span.StartLine);
    }

    /// <summary>A static constructor calls no base constructor, and a type whose base resolves has nothing missing.</summary>
    [Theory]
    [InlineData("class C : Missing { static int f; static C() { f = 1; } }", ".cctor")]
    [InlineData("class B { } class C : B { int f; C() { f = 1; } }", ".ctor")]
    [InlineData("class C { int f; C() { f = 1; } }", ".ctor")]
    public void AConstructorThatCallsNoUnresolvedBaseIsLowered(string source, string constructor) =>
        Assert.Empty(Opaques(Source(source, constructor, allowErrors: true)));

    /// <summary>
    /// A method that binds without error is lowered as it is bound, in a type with an unresolved base and when it calls a
    /// member of such a type (ADR 0029 as clarified): the callee's own pair is the one that is Unknown.
    /// </summary>
    [Theory]
    [InlineData("class C : Missing {\n public int M(int a) { return a + 1; }\n}")]
    [InlineData("class D : Missing { public int Get() { return Undefined(); } }\nclass C {\n public int M(D d) { return d.Get(); }\n}")]
    [InlineData("using Missing.Namespace;\nclass C {\n public int M(int a) { return a; }\n}")]
    public void AMethodThatBindsCleanlyInAnErroneousProjectIsLowered(string source)
    {
        IrProcedure procedure = Source(source, allowErrors: true);

        Assert.Empty(Opaques(procedure));
        Assert.Contains(procedure.Blocks, static b => b.Terminator is IrReturn { Value: not null });
    }

    [Fact]
    public void ACallToAMemberOfAnErroneousTypeIsStillACallToThatMember()
    {
        ImmutableArray<IrCall> calls = Calls(Source("class D : Missing { public int Get() { return Undefined(); } }\nclass C {\n public int M(D d) { return d.Get(); }\n}", allowErrors: true));

        Assert.Equal("D::Get()", Assert.Single(calls).Callee.Value);
    }
}
