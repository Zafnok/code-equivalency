using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-150: which members are storage of an explicit layout, which types the fingerprint writes a declaration for,
/// and which methods have an operation a type's layout fixes (<see cref="Layouts"/>).
/// </summary>
public sealed class LayoutsTests
{
    private const string Usings = "using System;\nusing System.Collections.Generic;\nusing System.Runtime.CompilerServices;\nusing System.Runtime.InteropServices;\n";

    /// <summary>
    /// Every instance field of an explicit layout is at an offset, and so is the backing field of an auto-property and of
    /// a field-like event. A static member is not, and neither is a member whose accessors are written out, or a method.
    /// </summary>
    [Theory]
    [InlineData("C", "A", true)]
    [InlineData("C", "Z", false)]
    [InlineData("C", "P", true)]
    [InlineData("C", "Q", false)]
    [InlineData("C", "E", true)]
    [InlineData("C", "F", false)]
    [InlineData("C", "G", false)]
    [InlineData("C", "M", false)]
    [InlineData("D", "A", false)]
    [InlineData("D", "P", false)]
    [InlineData("D", "E", false)]
    [InlineData("H", "E", false)]
    [InlineData("Q", "E", false)]
    public void AMemberOfAnExplicitLayoutThatTheCompilerStoresIsOverlaid(string type, string member, bool expected)
    {
        Compilation compilation = Compile(
            "[StructLayout(LayoutKind.Explicit)] class C { [FieldOffset(0)] public int A; public static int Z; [field: FieldOffset(8)] public int P { get; set; } public int Q => A; "
            + "[field: FieldOffset(16)] public event Action E; public static event Action F; public event Action G { add { } remove { } } public void M() { } } "
            + "class D { public int A; public int P { get; set; } public event Action E; } "
            + "[StructLayout(LayoutKind.Explicit)] abstract class H { [FieldOffset(0)] public int A; public abstract event Action E; } "
            + "[Serializable, StructLayout(LayoutKind.Sequential)] class Q { public event Action E; }");

        Assert.Equal(expected, Layouts.IsOverlaid(compilation.GetTypeByMetadataName(type)!.GetMembers(member).Single()));
    }

    /// <summary>
    /// A declaration is written for a type of the solution, reached through an array, a pointer, a function pointer's
    /// signature or a type argument. A type from a reference, and a type parameter, has none.
    /// </summary>
    [Theory]
    [InlineData("S", true)]
    [InlineData("S[]", true)]
    [InlineData("S*", true)]
    [InlineData("delegate*<S, int>", true)]
    [InlineData("delegate*<int, S>", true)]
    [InlineData("List<S[]>", true)]
    [InlineData("int", false)]
    [InlineData("int[]", false)]
    [InlineData("int*", false)]
    [InlineData("delegate*<int, int>", false)]
    [InlineData("List<int>", false)]
    [InlineData("T", false)]
    public void ATypeMadeOfOneTheSolutionDeclaresIsDeclared(string type, bool expected)
    {
        Compilation compilation = Compile("struct S { public int X; } unsafe class C<T> { " + type + " f; }");

        Assert.Equal(expected, Layouts.IsDeclared(((IFieldSymbol)compilation.GetTypeByMetadataName("C`1")!.GetMembers("f").Single()).Type));
    }

    /// <summary>
    /// A method reads a layout when its bound code, or an initializer its constructor runs, has an operation a
    /// declaration fixes: storage of an explicit layout, an operation the fingerprint writes a declaration after, or a
    /// call the runtime may marshal in an assembly that turns marshalling off. So does one that hands a generic member of
    /// the solution a type the solution declares, by the member it names or by the one a forwarder passes the call on to
    /// (ticket P2-154), and one that names a struct whose interface list hands a generic interface of the solution a
    /// type with a layout, or hands a member of the solution a class whose interface list does (ticket P2-161).
    /// </summary>
    [Theory]
    [InlineData("", "int M(U u) => u.P;", "M", true)]
    [InlineData("", "unsafe int M() => sizeof(S);", "M", true)]
    [InlineData("", "int M() => Marshal.SizeOf<S>();", "M", true)]
    [InlineData("", "unsafe int M(S* p) => p->X;", "M", true)]
    [InlineData("", "int n = Unsafe.SizeOf<S>(); C() { }", ".ctor", true)]
    [InlineData("", "int n = 1; C() { n = Marshal.SizeOf<int>(); }", ".ctor", false)]
    [InlineData("", "int M(S s) => s.X + Marshal.SizeOf<int>();", "M", false)]
    [InlineData("[assembly: DisableRuntimeMarshalling]\n", "int M() => Marshal.SizeOf<int>();", "M", true)]
    [InlineData("[assembly: DisableRuntimeMarshalling]\n", "int M(S s) => s.X;", "M", false)]
    [InlineData("", "[DllImport(\"a.dll\")] static extern int M();", "M", false)]
    [InlineData("", "int M() => Size<S>();", "M", true)]
    [InlineData("", "int M() => SizeOfS();", "M", true)]
    [InlineData("", "int M() => G<S>.Forward();", "M", true)]
    [InlineData("", "int M(G<S> g) => g.F;", "M", true)]
    [InlineData("", "G<S> M() => new G<S>();", "M", true)]
    [InlineData("", "int M<V>(G<V> g, G<C> c) => Size<int>() + Size<V>() + Size<C>() + g.F + c.F + Plain();", "M", false)]
    [InlineData("", "int M(Q q) => q.GetHashCode();", "M", true)]
    [InlineData("", "int M(Q[] q) => q.Length;", "M", true)]
    [InlineData("", "int M(R r, P p) => r.GetHashCode() + p.GetHashCode();", "M", false)]
    [InlineData("", "int M() => Size<QC>();", "M", true)]
    [InlineData("", "int M(G<QC[]> g) => g.F;", "M", true)]
    [InlineData("", "int M(QC q, G<PC> g) => Size<PC>() + g.F + q.GetHashCode();", "M", false)]
    public void AMethodWithAnOperationADeclarationFixesReadsALayout(string assembly, string member, string name, bool expected)
    {
        Compilation compilation = Compile(
            assembly + "struct S { public int X; } [StructLayout(LayoutKind.Explicit)] class U { [field: FieldOffset(0)] public int P { get; set; } } "
            + "class G<T> { public int F; public static int Forward() => C.Plain(); } "
            + "interface J<T> { } struct Q : J<S> { } struct R : J<int> { } struct P : J<C> { } class QC : J<S> { } class PC : J<C> { } "
            + "class C { static int Size<T>() => 0; static int SizeOfS() => Size<S>(); public static int Plain() => 0; " + member + " }");

        Assert.Equal(expected, IrLowerer.DependsOnLayout(compilation.GetTypeByMetadataName("C")!.GetMembers(name).OfType<IMethodSymbol>().Single(), compilation));
    }

    /// <summary>
    /// Ticket P2-154: code that has a type as a type argument reads the layout of a value type of the solution and of a
    /// class that has a <c>[StructLayout]</c>, reached as a declaration is. Of any other type it reads none.
    /// </summary>
    [Theory]
    [InlineData("S", true)]
    [InlineData("E", true)]
    [InlineData("L", true)]
    [InlineData("S[]", true)]
    [InlineData("S*[]", true)]
    [InlineData("delegate*<S, int>[]", true)]
    [InlineData("List<(K, S)>", true)]
    [InlineData("K", false)]
    [InlineData("I", false)]
    [InlineData("K[]", false)]
    [InlineData("List<(K, int)>", false)]
    [InlineData("Guid", false)]
    [InlineData("T", false)]
    public void ATypeMadeOfAValueTypeTheSolutionDeclaresIsLaidOut(string type, bool expected)
    {
        Compilation compilation = Compile(
            "struct S { public int X; } enum E { None } class K { } interface I { } [StructLayout(LayoutKind.Sequential)] class L { } unsafe class C<T> { " + type + " f; }");

        Assert.Equal(expected, Layouts.IsLaidOut(((IFieldSymbol)compilation.GetTypeByMetadataName("C`1")!.GetMembers("f").Single()).Type));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Compilation compilation = Compile("class C { int M() => 0; }");
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IOperation operation = compilation.GetSemanticModel(compilation.SyntaxTrees.Single()).GetOperation(method.DeclaringSyntaxReferences[0].GetSyntax(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)!;

        Assert.Throws<ArgumentNullException>("operation", () => Layouts.Reached(null!));
        Assert.Throws<ArgumentNullException>("operation", () => Layouts.TypeArguments(null!, static callee => callee));
        Assert.Throws<ArgumentNullException>("called", () => Layouts.TypeArguments(operation, null!));
        Assert.Throws<ArgumentNullException>("types", () => Layouts.Implemented(null!, []));
        Assert.Throws<ArgumentNullException>("handed", () => Layouts.Implemented([], null!));
        Assert.Throws<ArgumentNullException>("operation", () => Layouts.Inherits(null!));
        Assert.Throws<ArgumentNullException>("operation", () => Layouts.IsHanded(null!, compilation.Assembly));
        Assert.Throws<ArgumentNullException>("assembly", () => Layouts.IsHanded(operation, null!));
        Assert.Throws<ArgumentNullException>("operation", () => Layouts.Reads(null!, compilation.Assembly));
        Assert.Throws<ArgumentNullException>("assembly", () => Layouts.Reads(operation, null!));
        Assert.Throws<ArgumentNullException>("member", () => Layouts.IsOverlaid(null!));
        Assert.Throws<ArgumentNullException>("raised", () => Layouts.IsStored(null!));
        Assert.Throws<ArgumentNullException>("method", () => IrLowerer.DependsOnLayout(null!, compilation));
        Assert.Throws<ArgumentNullException>("compilation", () => IrLowerer.DependsOnLayout(method, null!));
    }

    private static Compilation Compile(string source)
    {
        Compilation compilation = RoslynTestCompilations.Compile(Usings + source);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return compilation;
    }
}
