using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-099: a collection expression lowers as the construct it replaces. An array is the array creation, and with
/// no element the <c>Array.Empty</c> call; a <c>List&lt;T&gt;</c> is its constructor and one <c>Add</c> per element; a
/// read-only interface is the array through the <c>cast</c> map. Every other target stays opaque with reason
/// <c>CollectionExpression</c>.
/// </summary>
public sealed class CollectionExpressionLoweringTests
{
    /// <summary>A <c>List&lt;T&gt;</c> is the parameterless constructor, then each element evaluated and added in order.</summary>
    [Theory]
    [InlineData("static List<int> M() => [];", new[] { "System.Collections.Generic.List`1::.ctor()<int>" })]
    [InlineData(
        "static int A() => 1; static int B() => 2; static List<int> M() => [A(), B()];",
        new[] { "System.Collections.Generic.List`1::.ctor()<int>", "C::A()", "System.Collections.Generic.List`1::Add(int)<int>", "C::B()", "System.Collections.Generic.List`1::Add(int)<int>" })]
    public void ACollectionExpressionForAListIsItsConstructorAndItsAdds(string members, string[] calls)
    {
        IrProcedure procedure = Generic(members);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(calls, Calls(procedure).Select(static c => c.Callee.Value), StringComparer.Ordinal);
    }

    /// <summary>Each <c>Add</c> takes the list the constructor yielded, then the element.</summary>
    [Fact]
    public void EachAddTakesTheNewList()
    {
        IrProcedure procedure = Generic("static List<int> M(int a) => [a];");

        Assert.Equal([Calls(procedure)[0].Target!.Name, "a"], Calls(procedure)[1].Args.Select(static a => a.SourceName ?? a.Name), StringComparer.Ordinal);
    }

    /// <summary>A collection expression lowers exactly as the list it replaces does once the constructor has returned.</summary>
    [Theory]
    [InlineData("static List<int> M(int a, int b) => new List<int> { a, b };", "static List<int> M(int a, int b) => [a, b];")]
    [InlineData("static List<string> M() { List<string> l = new(); return l; }", "static List<string> M() { List<string> l = []; return l; }")]
    public void AListLowersToTheCallsItsInitializerMakes(string legacy, string modern) =>
        Assert.Equal(
            Calls(Generic(legacy)).Select(static c => c.Callee.Value),
            Calls(Generic(modern)).Select(static c => c.Callee.Value),
            StringComparer.Ordinal);

    /// <summary>With no element an array is the <c>Array.Empty</c> call the compiler emits, whatever the element type.</summary>
    [Theory]
    [InlineData("static int[] M() => [];", "System.Array::Empty`1()<int>")]
    [InlineData("struct S { int x; } static S[] M() => [];", "System.Array::Empty`1()<C.S>")]
    [InlineData("static IEnumerable<string> M() => [];", "System.Array::Empty`1()<string>")]
    public void AnEmptyArrayIsTheArrayEmptyCall(string members, string callee)
    {
        IrProcedure procedure = Generic(members);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(callee, Assert.Single(Calls(procedure)).Callee.Value);
    }

    /// <summary>The empty array lowers exactly as the call it replaces, the conversion to an interface included.</summary>
    [Theory]
    [InlineData("static int[] M() => Array.Empty<int>();", "static int[] M() => [];")]
    [InlineData("static IEnumerable<int> M() => Array.Empty<int>();", "static IEnumerable<int> M() => [];")]
    [InlineData("static int M() { int[] a = Array.Empty<int>(); return a.Length; }", "static int M() { int[] a = []; return a.Length; }")]
    public void AnEmptyArrayLowersAsTheArrayEmptyCallDoes(string legacy, string modern) =>
        Assert.Equal(IrText.Dump(Generic(legacy)), IrText.Dump(Generic(modern)));

    /// <summary>A read-only interface target is the array, read through the map an array's conversion to it reads.</summary>
    [Theory]
    [InlineData("IEnumerable<int>")]
    [InlineData("IReadOnlyCollection<int>")]
    [InlineData("IReadOnlyList<int>")]
    public void AReadOnlyInterfaceIsTheArrayThroughItsCast(string type) =>
        Assert.Equal(
            IrText.Dump(Generic($"static {type} M(int a, int b) => new int[] {{ a, b }};")),
            IrText.Dump(Generic($"static {type} M(int a, int b) => [a, b];")));

    /// <summary>
    /// A value is known not to be null exactly where the old form's is: a creation or a <c>new</c> needs no null test, and
    /// an <c>Array.Empty</c> result or a value read through a cast map asks its <c>null</c> map, as on the old side.
    /// </summary>
    [Theory]
    [InlineData("static int M(int a) { int[] x = [a]; return x.Length; }", false)]
    [InlineData("static int M(int a) { List<int> x = [a]; return x.Count; }", false)]
    [InlineData("static int M() { int[] x = []; return x.Length; }", true)]
    [InlineData("static int M(int a) { IReadOnlyList<int> x = [a]; return x.Count; }", true)]
    [InlineData("static int M(int[] a) { int[] x = [.. a]; return x.Length; }", true)]
    public void ACollectionIsNeverNullWhereItsOldFormIsNot(string members, bool asksTheNullMap) =>
        Assert.Equal(asksTheNullMap, Generic(members).Parameters.Any(static p => p.Var.Name.StartsWith("null.", StringComparison.Ordinal)));

    /// <summary>
    /// A target-typed <c>new()</c> is the creation it spells: the constructor call and no opaque, as <c>new T()</c> is, so
    /// it can be compared with the <c>[]</c> that replaces it.
    /// </summary>
    [Theory]
    [InlineData("static List<string> M() { List<string> l = new(); return l; }", "static List<string> M() { List<string> l = new List<string>(); return l; }")]
    [InlineData("static object M() { object o = new(); return o; }", "static object M() { object o = new object(); return o; }")]
    [InlineData("struct S { public int X; } static S M() => new();", "struct S { public int X; } static S M() => new S();")]
    public void ATargetTypedNewIsItsCreation(string targetTyped, string spelled)
    {
        IrProcedure procedure = Generic(targetTyped);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(IrText.Dump(Generic(spelled)), IrText.Dump(procedure));
    }

    /// <summary>A target-typed <c>new()</c> for a <c>Nullable&lt;T&gt;</c> creates a <c>T</c> and converts it, and stays opaque.</summary>
    [Fact]
    public void ATargetTypedNewOfANullableStructStaysOpaque() =>
        Assert.Equal("Conversion", Assert.Single(Opaques(Generic("struct S { public int X; } static S? M() => new();"))).Reason);

    /// <summary>
    /// Criterion 4, and the targets out of scope: a <c>CollectionBuilder</c> type, a span, a mutable interface, a class
    /// other than <c>List&lt;T&gt;</c>, a type parameter, and an array the array creation would not create, stay opaque.
    /// </summary>
    [Theory]
    [InlineData("static System.Collections.Immutable.ImmutableArray<int> M(int a) => [a];")]
    [InlineData("static int M(int a) { Span<int> s = [a]; return s.Length; }")]
    [InlineData("static IList<int> M(int a) => [a];")]
    [InlineData("static ICollection<int> M(int a) => [a];")]
    [InlineData("static HashSet<int> M(int a) => [a];")]
    [InlineData("static T M<T>(int a) where T : ICollection<int>, new() => [a];")]
    [InlineData("static int[][] M(int[] a) => [a];")]
    [InlineData("struct S { int x; } static S[] M(S s) => [s];")]
    public void AnyOtherCollectionExpressionIsOpaque(string members) =>
        Assert.Equal("CollectionExpression", Assert.Single(Opaques(Generic(members))).Reason);

    /// <summary>
    /// On a framework with no <c>Array.Empty</c> (before .NET Framework 4.6) the compiler emits <c>new T[0]</c>, and so
    /// does the lowering: a creation of length 0 and no call.
    /// </summary>
    [Fact]
    public void AnEmptyArrayIsACreationWhereTheFrameworkHasNoArrayEmpty()
    {
        const string Source = """
            namespace System
            {
                public class Object { }
                public abstract class ValueType { }
                public abstract class Enum : ValueType { }
                public class Attribute { }
                public struct Void { }
                public struct Boolean { }
                public struct Int32 { }
                public class String { }
                public abstract class Array { }
                public class Exception { }
            }

            class C
            {
                static int[] M() => [];
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "NoArrayEmpty",
            [CSharpSyntaxTree.ParseText(Source, cancellationToken: TestContext.Current.CancellationToken)],
            [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration);

        Assert.Empty(IrValidator.Validate(procedure));
        Assert.Empty(Opaques(procedure));
        Assert.Empty(Calls(procedure));
        Assert.Contains(procedure.Parameters, static p => p.Var.Name.StartsWith("new.", StringComparison.Ordinal));
    }

    /// <summary>Lowers <c>M</c> of class <c>C</c> with <c>System.Collections.Generic</c> in scope.</summary>
    private static IrProcedure Generic(string members) =>
        Source($"using System;\nusing System.Collections.Generic;\nclass C\n{{\n{members}\n}}\n");
}
