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
    private const string Usings = "using System.Collections.Generic;\nusing System.Linq;\nusing System.Runtime.CompilerServices;\nusing System.Runtime.InteropServices;\n";

    private const string Generic = "static int Size<T>() => Unsafe.SizeOf<T>(); sealed class Box<T> { public static int Size() => Unsafe.SizeOf<T>(); public static int Width => Unsafe.SizeOf<T>(); public int Count => Unsafe.SizeOf<T>(); }";

    private const string Overlaid = "[StructLayout(LayoutKind.Explicit)] class U { [FieldOffset(0)] public int A; [FieldOffset(0)] public int B; } ";

    private const string Implementing = "interface W { int Width(); } interface J<T> : W { int W.Width() => Unsafe.SizeOf<T>(); } struct K : J<S> { }";

    private const string Making = "static W Make<T>() where T : struct, W => default(T);";

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

    /// <summary>
    /// Ticket P2-153: beside one declaration, the same reads and writes of an explicit layout's storage in the same
    /// order are the same calls on both sides, so a pair that differs elsewhere is still proved; beside two declarations
    /// it is not.
    /// </summary>
    [Theory]
    [InlineData("[FieldOffset(0)] public int A; [FieldOffset(0)] public int B;", "[FieldOffset(0)] public int A; [FieldOffset(4)] public int B;")]
    [InlineData("[field: FieldOffset(0)] public int A { get; set; } [field: FieldOffset(0)] public int B { get; set; }", "[field: FieldOffset(0)] public int A { get; set; } [field: FieldOffset(4)] public int B { get; set; }")]
    public void TheSameUseOfAnExplicitLayoutIsSharedBesideOneDeclarationOnly(string members, string moved)
    {
        const string Legacy = "class C { static int M(U s, int x) { s.A = x; return s.B + 1; } }";
        const string Modern = "class C { static int M(U s, int x) { s.A = x; return 1 + s.B; } }";
        static string Declared(string members) => "[StructLayout(LayoutKind.Explicit)] sealed class U { " + members + " } ";

        Assert.IsType<Equivalent>(Verify(Declared(members) + Legacy, Declared(members) + Modern));
        Assert.IsNotType<Equivalent>(Verify(Declared(members) + Legacy, Declared(moved) + Modern));
    }

    /// <summary>
    /// Ticket P2-154: a generic member of the solution reads the layout of the type it is handed, and its own text is
    /// the same for every type argument. <c>Size&lt;S&gt;()</c> is 8 beside the first declaration and 5 beside the
    /// second, in bodies that differ elsewhere, and the call's identity names <c>S</c> and nothing of its declaration.
    /// </summary>
    [Theory]
    [InlineData("Size<S>()")]
    [InlineData("Box<S>.Size()")]
    [InlineData("Box<S>.Width")]
    [InlineData("new Box<S>().Count")]
    public void ACallThatHandsAGenericMemberATypeIsNotOneFunctionBesideTwoDeclarations(string call) =>
        Assert.IsNotType<Equivalent>(Verify(
            $"struct S {{ public byte A; public int B; }} class C {{ {Generic} static int M(int x) => {call} + x; }}",
            $"[StructLayout(LayoutKind.Sequential, Pack = 1)] struct S {{ public byte A; public int B; }} class C {{ {Generic} static int M(int x) => x + {call}; }}"));

    /// <summary>Beside one declaration the call is the same on both sides, as it was.</summary>
    [Theory]
    [InlineData("Size<S>()")]
    [InlineData("Box<S>.Size()")]
    public void ACallThatHandsAGenericMemberATypeIsStillSharedBesideOneDeclaration(string call)
    {
        const string Declared = "struct S { public byte A; public int B; } ";

        Assert.IsType<Equivalent>(Verify(
            Declared + $"class C {{ {Generic} static int M(int x) => {call} + x; }}",
            Declared + $"class C {{ {Generic} static int M(int x) => x + {call}; }}"));
    }

    /// <summary>
    /// Ticket P2-161: a struct's interface list hands a generic interface of the solution a type, and the code the
    /// struct inherits from it reads the type's layout. <c>Width()</c> is 8 beside the first declaration of <c>S</c> and
    /// 5 beside the second, in bodies that differ elsewhere, and no operation of the body names <c>S</c>. A body that
    /// makes the struct was not Equivalent before the ticket either: the <c>default</c>, the array creation and the call
    /// that is handed <c>K</c> were opaques already, whose fingerprints hold the declaration (ticket P2-157).
    /// </summary>
    [Theory]
    [InlineData("((W)default(K)).Width()")]
    [InlineData("Make<K>().Width()")]
    [InlineData("((W)(new K[1])[0]).Width()")]
    public void ABodyThatNamesAStructIsNotTheSameBesideTwoDeclarationsOfWhatItsInterfaceListHandsOver(string call) =>
        Assert.IsNotType<Equivalent>(Verify(
            $"struct S {{ public byte A; public int B; }} {Implementing} class C {{ {Making} static int M(int x) => {call} + x; }}",
            $"[StructLayout(LayoutKind.Sequential, Pack = 1)] struct S {{ public byte A; public int B; }} {Implementing} class C {{ {Making} static int M(int x) => x + {call}; }}"));

    /// <summary>Beside one declaration the body is the same on both sides, as it was.</summary>
    [Theory]
    [InlineData("((W)default(K)).Width()")]
    [InlineData("Make<K>().Width()")]
    public void ABodyThatNamesAStructIsStillSharedBesideOneDeclaration(string call)
    {
        const string Declared = "struct S { public byte A; public int B; } " + Implementing;

        Assert.IsType<Equivalent>(Verify(
            Declared + $" class C {{ {Making} static int M(int x) => {call} + x; }}",
            Declared + $" class C {{ {Making} static int M(int x) => x + {call}; }}"));
    }

    /// <summary>
    /// Ticket P2-161: the same where no operation of the body makes the struct: it is a parameter, a field no body
    /// assigns, an array's element, a list handed to a member of a reference, a value handed on to a member of the
    /// solution, an <c>out</c> argument or a loop's variable. Beside one declaration each is the same on both sides.
    /// </summary>
    [Theory]
    [InlineData("static int M(K k, int x) => ((W)k).Width() + x;", "static int M(K k, int x) => x + ((W)k).Width();")]
    [InlineData("static K Unset; static int M(int x) => ((W)Unset).Width() + x;", "static K Unset; static int M(int x) => x + ((W)Unset).Width();")]
    [InlineData("static int M(K[] k, int x) => ((W)k[0]).Width() + x;", "static int M(K[] k, int x) => x + ((W)k[0]).Width();")]
    [InlineData("static int M(int x) => Enumerable.First(Enumerable.Cast<W>(new List<K>(1))).Width() + x;", "static int M(int x) => x + Enumerable.First(Enumerable.Cast<W>(new List<K>(1))).Width();")]
    [InlineData("static int M(K k, int x) => Take(k) + x;", "static int M(K k, int x) => x + Take(k);")]
    [InlineData("static int M(K k, K j, int x) => Take(x > 0 ? k : j) + x;", "static int M(K k, K j, int x) => x + Take(x > 0 ? k : j);")]
    [InlineData("static int M(int x) { Out(out K l); return Take(l) + x; }", "static int M(int x) { Out(out K l); return x + Take(l); }")]
    [InlineData("static int M(K[] ks, int x) { int n = 0; foreach (K k in ks) { n += Take(k); } return n + x; }", "static int M(K[] ks, int x) { int n = 0; foreach (K k in ks) { n += Take(k); } return x + n; }")]
    public void ABodyThatIsHandedAStructIsTheSameBesideOneDeclarationOnlyOfWhatItsInterfaceListHandsOver(string legacy, string modern)
    {
        const string Declared = "struct S { public byte A; public int B; } ";
        const string Packed = "[StructLayout(LayoutKind.Sequential, Pack = 1)] " + Declared;
        const string Taking = " static int Take(W w) => w.Width(); static int Out(out K k) { k = default; return 1; } ";

        Assert.IsType<Equivalent>(Verify($"{Declared}{Implementing} class C {{ {Taking}{legacy} }}", $"{Declared}{Implementing} class C {{ {Taking}{modern} }}"));
        Assert.IsNotType<Equivalent>(Verify($"{Declared}{Implementing} class C {{ {Taking}{legacy} }}", $"{Packed}{Implementing} class C {{ {Taking}{modern} }}"));
    }

    /// <summary>
    /// Tickets P2-162 and P2-161: a class is handed to a generic member of the solution, which creates it, and the
    /// code the class inherits from its interface list reads the layout of <c>S</c>. The class has no layout of its
    /// own, and the call's identity names it alone. Beside one declaration the call is the same on both sides.
    /// </summary>
    [Theory]
    [InlineData("Create<Q>().Width()")]
    [InlineData("Holder<Q>.Size()")]
    public void ACallThatHandsAMemberAClassIsNotOneFunctionBesideTwoDeclarationsOfWhatItsInterfaceListHandsOver(string call)
    {
        const string Declared = "struct S { public byte A; public int B; } ";
        const string Packed = "[StructLayout(LayoutKind.Sequential, Pack = 1)] " + Declared;
        const string Creating = Implementing + " class Q : J<S> { } class Holder<T> where T : W, new() { public static int Size() => new T().Width(); }"
            + " class C { static W Create<T>() where T : W, new() => new T(); ";

        Assert.IsType<Equivalent>(Verify($"{Declared}{Creating} static int M(int x) => {call} + x; }}", $"{Declared}{Creating} static int M(int x) => x + {call}; }}"));
        Assert.IsNotType<Equivalent>(Verify($"{Declared}{Creating} static int M(int x) => {call} + x; }}", $"{Packed}{Creating} static int M(int x) => x + {call}; }}"));
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
