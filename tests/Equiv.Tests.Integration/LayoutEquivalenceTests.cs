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
/// Ticket P2-150 criterion 1 end to end: a body that reads a type's layout is not proved Equivalent to one that differs
/// from it in what the layout decides. Each pair here was Equivalent while a field of an explicit layout was its own map
/// and a call that reads a layout was one function for both sides.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LayoutEquivalenceTests
{
    private const string Usings = "using System.Runtime.CompilerServices;\nusing System.Runtime.InteropServices;\n";

    private const string Overlaid = "[StructLayout(LayoutKind.Explicit)] class U { [FieldOffset(0)] public int A; [FieldOffset(0)] public int B; } ";

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    /// <summary>
    /// Two fields at one <c>[FieldOffset]</c> are one storage location: the first body returns the 1 it wrote and the
    /// second what the field held before. The two declarations are the same.
    /// </summary>
    [Theory]
    [InlineData("static int M(U s) { s.A = 1; return s.B; }", "static int M(U s) { int b = s.B; s.A = 1; return b; }")]
    [InlineData("static int M(U s) { s.A += 1; return s.B; }", "static int M(U s) { int b = s.B; s.A += 1; return b; }")]
    [InlineData("static int M(U s) { s.A++; return s.B; }", "static int M(U s) { int b = s.B; s.A++; return b; }")]
    [InlineData("static int M(U s) { (s.A, _) = (1, 2); return s.B; }", "static int M(U s) { int b = s.B; (s.A, _) = (1, 2); return b; }")]
    public void AWriteToOneFieldOfAnExplicitLayoutIsNotProvedToLeaveAnotherAlone(string legacy, string modern) =>
        Assert.IsNotType<Equivalent>(Verify(Overlaid + $"class C {{ {legacy} }}", Overlaid + $"class C {{ {modern} }}"));

    /// <summary>The same through an auto-property, whose backing field has the offset.</summary>
    [Fact]
    public void AWriteToOneAutoPropertyOfAnExplicitLayoutIsNotProvedToLeaveAnotherAlone()
    {
        const string Properties = "[StructLayout(LayoutKind.Explicit)] sealed class U { [field: FieldOffset(0)] public int A { get; set; } [field: FieldOffset(0)] public int B { get; set; } } ";

        Assert.IsNotType<Equivalent>(Verify(
            Properties + "class C { static int M(U s) { s.A = 1; return s.B; } }",
            Properties + "class C { static int M(U s) { int b = s.B; s.A = 1; return b; } }"));
    }

    /// <summary>
    /// A call that is handed a type reads its declaration: <c>Marshal.SizeOf&lt;S&gt;()</c> is 8 beside the first
    /// declaration and 5 beside the second. The bodies differ elsewhere, so the pair is not congruent.
    /// </summary>
    [Theory]
    [InlineData("Marshal.SizeOf<S>()")]
    [InlineData("Marshal.SizeOf(typeof(S))")]
    [InlineData("Unsafe.SizeOf<S>()")]
    [InlineData("MemoryMarshal.Cast<byte, S>(new byte[16]).Length")]
    [InlineData("new System.Span<S>(new S[2]).Length * Unsafe.SizeOf<S>()")]
    public void ACallThatReadsATypesLayoutIsNotOneFunctionBesideTwoDeclarations(string call) =>
        Assert.IsNotType<Equivalent>(Verify(
            $"struct S {{ public byte A; public int B; }} class C {{ static int M(int x) => {call} + x; }}",
            $"[StructLayout(LayoutKind.Sequential, Pack = 1)] struct S {{ public byte A; public int B; }} class C {{ static int M(int x) => x + {call}; }}"));

    /// <summary>Beside one declaration the call is the same function on both sides, as it was.</summary>
    [Theory]
    [InlineData("Marshal.SizeOf<S>()")]
    [InlineData("Unsafe.SizeOf<S>()")]
    public void ACallThatReadsATypesLayoutIsStillSharedBesideOneDeclaration(string call)
    {
        const string Declared = "struct S { public byte A; public int B; } ";

        Assert.IsType<Equivalent>(Verify(
            Declared + $"class C {{ static int M(int x) => {call} + x; }}",
            Declared + $"class C {{ static int M(int x) => x + {call}; }}"));
    }

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower(legacy, isLegacy: true), Lower(modern, isLegacy: false), Options);

    private static IrProcedure Lower(string source, bool isLegacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText(Usings + source, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, Runtimes.Migration).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
