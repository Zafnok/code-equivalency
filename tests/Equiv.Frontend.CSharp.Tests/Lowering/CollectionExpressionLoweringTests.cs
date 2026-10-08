using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-099: a collection expression lowers as the construct it replaces. An array is the array creation, and with
/// no element the <c>Array.Empty</c> call; a <c>List&lt;T&gt;</c> is a new list (ADR 0043) and one <c>Add</c> per element; a
/// read-only interface is the array through the <c>cast</c> map. Ticket P2-120: any class with a parameterless constructor
/// and one <c>Add</c> is built as a <c>List&lt;T&gt;</c> is, <c>IList&lt;T&gt;</c> and <c>ICollection&lt;T&gt;</c> are a
/// <c>List&lt;T&gt;</c> through the <c>cast</c> map, and an element the control flow graph evaluates ahead of the
/// expression is still added before the next one is evaluated. Every other target stays opaque with reason
/// <c>CollectionExpression</c>.
/// </summary>
public sealed class CollectionExpressionLoweringTests
{
    /// <summary>A <c>List&lt;T&gt;</c> is a new list, which is no call (ADR 0043), then each element evaluated and added in order.</summary>
    [Theory]
    [InlineData("static List<int> M() => [];", new string[0])]
    [InlineData(
        "static int A() => 1; static int B() => 2; static List<int> M() => [A(), B()];",
        new[] { "C::A()", "System.Collections.Generic.List`1::Add(int)<int>", "C::B()", "System.Collections.Generic.List`1::Add(int)<int>" })]
    public void ACollectionExpressionForAListIsANewListAndItsAdds(string members, string[] calls)
    {
        IrProcedure procedure = Generic(members);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(calls, Calls(procedure).Select(static c => c.Callee.Value), StringComparer.Ordinal);
        Assert.Contains(procedure.Parameters, static p => p.Var.Name.StartsWith("new.", StringComparison.Ordinal));
    }

    /// <summary>Each <c>Add</c> takes the new list, which is the value of the expression, then the element.</summary>
    [Fact]
    public void EachAddTakesTheNewList()
    {
        IrProcedure procedure = Generic("static List<int> M(int a) => [a];");

        IrCall add = Assert.Single(Calls(procedure));
        IrReturn exit = Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrReturn>());
        Assert.Equal(exit.Value, add.Args[0]);
        Assert.Equal("a", add.Args[1].SourceName);
    }

    /// <summary>A collection expression makes exactly the calls the list it replaces does.</summary>
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
    /// P2-120 criterion 2: an element with an object initializer, a <c>??</c> or a conditional is evaluated into a flow
    /// capture ahead of the collection expression, and so is every element before it. The object is still made first and
    /// each element added before the next is evaluated, so the calls are the collection initializer's, in its order.
    /// </summary>
    [Theory]
    [InlineData("static List<X> M(int a, int b) => new List<X> { new X { P = a }, new X { P = b } };", "static List<X> M(int a, int b) => [new X { P = a }, new X { P = b }];")]
    [InlineData("static List<string> M(string a) => new List<string> { F() ?? a, G() ?? a };", "static List<string> M(string a) => [F() ?? a, G() ?? a];")]
    [InlineData("static List<string> M(string a) => new List<string> { F(), G() ?? a, F() };", "static List<string> M(string a) => [F(), G() ?? a, F()];")]
    [InlineData("static List<string> M(bool c) => new List<string> { F(), c ? G() : F(), G() };", "static List<string> M(bool c) => [F(), c ? G() : F(), G()];")]
    [InlineData(
        "static List<string> M(string a, bool c) { if (c) { return new List<string> { F(), G() ?? a }; } return null; }",
        "static List<string> M(string a, bool c) { if (c) { return [F(), G() ?? a]; } return null; }")]
    [InlineData("static Bag M(string a) => new Bag { F(), G() ?? a };", "static Bag M(string a) => [F(), G() ?? a];")]
    [InlineData("static IList<string> M(string a) => new List<string> { F(), G() ?? a };", "static IList<string> M(string a) => [F(), G() ?? a];")]
    [InlineData(
        "static List<List<X>> M(int a, int b) => new List<List<X>> { new List<X> { new X { P = a } }, new List<X> { new X { P = b }, new X() } };",
        "static List<List<X>> M(int a, int b) => [[new X { P = a }], [new X { P = b }, new X()]];")]
    [InlineData(
        "static void M(string a) { for (int i = 0; i < 2; i++) { Use(new List<string> { F(), G() ?? a }); } }",
        "static void M(string a) { for (int i = 0; i < 2; i++) { Use([F(), G() ?? a]); } }")]
    [InlineData(
        "static void M(string a) { try { F(); } finally { Use(new List<string> { F(), G() ?? a }); } }",
        "static void M(string a) { try { F(); } finally { Use([F(), G() ?? a]); } }")]
    public void ElementsEvaluatedAheadAreAddedInTheInitializersOrder(string legacy, string modern)
    {
        IrProcedure lowered = Generic(Elements + modern);

        Assert.Empty(Opaques(lowered));
        Assert.Equal(Trace(Generic(Elements + legacy)), Trace(lowered));
    }

    /// <summary>Each <c>Add</c> follows its element's calls, and the object's constructor, when it is a call, is ahead of the first element.</summary>
    [Fact]
    public void EachAddFollowsItsElement()
    {
        string[] calls = Trace(Generic(Elements + "static Bag M(string a) => [F() ?? a, G() ?? a];"));

        Assert.Equal(["C.Bag::.ctor()", "C::F()", "C.Bag::Add(string)", "C::G()", "C.Bag::Add(string)"], calls.Select(static c => c.Split('/')[0]), StringComparer.Ordinal);
    }

    /// <summary>The object made where the first element starts is the one every <c>Add</c> takes and the expression's value.</summary>
    [Fact]
    public void TheObjectMadeAheadIsTheOneAddedToAndReturned()
    {
        IrProcedure procedure = Generic(Elements + "static Bag M(string a) => [F() ?? a, G() ?? a];");

        IrVar made = Calls(procedure).Single(static c => string.Equals(c.Callee.Value, "C.Bag::.ctor()", StringComparison.Ordinal)).Target!;
        Assert.All(Calls(procedure).Where(static c => string.Equals(c.Callee.Value, "C.Bag::Add(string)", StringComparison.Ordinal)), add => Assert.Equal(made, add.Args[0]));
        Assert.Equal(made, Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrReturn>()).Value);
    }

    /// <summary>
    /// P2-120 criterion 3: <c>IList&lt;T&gt;</c> and <c>ICollection&lt;T&gt;</c> are the <c>List&lt;T&gt;</c> the compiler
    /// builds, read through the map the list's conversion to the interface reads.
    /// </summary>
    [Theory]
    [InlineData("IList<int>", "IList_1")]
    [InlineData("ICollection<int>", "ICollection_1")]
    public void AMutableInterfaceIsAListThroughItsCast(string type, string sort)
    {
        IrProcedure procedure = Generic($"static {type} M(int a, int b) => [a, b];");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(Trace(Generic($"static {type} M(int a, int b) => new List<int> {{ a, b }};")), Trace(procedure));
        Assert.Contains(procedure.Parameters, p => string.Equals(p.Var.Name, $"cast.System.Collections.Generic.List_1.System.Collections.Generic.{sort}", StringComparison.Ordinal));
    }

    /// <summary>
    /// P2-120 criterion 3: a class with a parameterless constructor and one <c>Add</c> of the elements' type makes the
    /// calls its collection initializer makes, whether the <c>Add</c> is its own or a base type's, and whatever it returns.
    /// </summary>
    [Theory]
    [InlineData("static HashSet<int> M(int a, int b) => new HashSet<int> { a, b };", "static HashSet<int> M(int a, int b) => [a, b];")]
    [InlineData("static Bag M(string a) => new Bag { a, F() };", "static Bag M(string a) => [a, F()];")]
    [InlineData("static Derived M(string a) => new Derived { a, F() };", "static Derived M(string a) => [a, F()];")]
    [InlineData("static Wide M(int a) => new Wide { a };", "static Wide M(int a) => [a];")]
    [InlineData(
        "static System.Collections.ObjectModel.ObservableCollection<string> M(string a) => new System.Collections.ObjectModel.ObservableCollection<string> { a };",
        "static System.Collections.ObjectModel.ObservableCollection<string> M(string a) => [a];")]
    public void AClassIsBuiltAsItsCollectionInitializerIs(string legacy, string modern)
    {
        IrProcedure lowered = Generic(Elements + modern);

        Assert.Empty(Opaques(lowered));
        Assert.NotEmpty(Calls(lowered));
        Assert.Equal(Trace(Generic(Elements + legacy)), Trace(lowered));
    }

    /// <summary>An <c>Add</c> that returns a value is called for it, as the initializer's is.</summary>
    [Fact]
    public void AnAddThatReturnsAValueYieldsIt() =>
        Assert.Equal(new IrBool(), Assert.Single(Calls(Generic("static HashSet<int> M(int a) => [a];"))).Target?.Type);

    /// <summary>
    /// With no element nothing is added, so any class with a parameterless constructor is <c>new T()</c>, a
    /// <c>Dictionary&lt;K, V&gt;</c> or a class with two <c>Add</c> methods included.
    /// </summary>
    [Theory]
    [InlineData("static Dictionary<string, int> M() => new Dictionary<string, int>();", "static Dictionary<string, int> M() => [];")]
    [InlineData("static HashSet<int> M() => new HashSet<int>();", "static HashSet<int> M() => [];")]
    [InlineData("static Bag M() => new Bag();", "static Bag M() => [];")]
    [InlineData("static TwoAdds M() => new TwoAdds();", "static TwoAdds M() => [];")]
    [InlineData("static IList<int> M() => new List<int>();", "static IList<int> M() => [];")]
    [InlineData("static int M() { Dictionary<string, int> d = new(); return d.Count; }", "static int M() { Dictionary<string, int> d = []; return d.Count; }")]
    public void AnEmptyClassIsItsCreation(string legacy, string modern)
    {
        IrProcedure lowered = Generic(Elements + modern);

        Assert.Empty(Opaques(lowered));
        Assert.Equal(IrText.Dump(Generic(Elements + legacy)), IrText.Dump(lowered));
    }

    /// <summary>
    /// The targets that stay opaque: a <c>CollectionBuilder</c> type, a <c>Span&lt;T&gt;</c>, a type parameter, a struct, a class whose
    /// constructor takes an argument, one with two <c>Add</c> methods, and an array the array creation would not create.
    /// </summary>
    [Theory]
    [InlineData("static System.Collections.Immutable.ImmutableArray<int> M(int a) => [a];")]
    [InlineData("static int M(int a) { Span<int> s = [a]; return s.Length; }")]
    [InlineData("static T M<T>(int a) where T : ICollection<int>, new() => [a];")]
    [InlineData("static TwoAdds M(string a) => [a];")]
    [InlineData("static Sized M(string a) => [a];")]
    [InlineData("static Sized M() => [];")]
    [InlineData("static Pocket M(string a) => [a];")]
    [InlineData("static Pocket M() => [];")]
    [InlineData("static int[][] M(int[] a) => [a];")]
    [InlineData("struct S { int x; } static S[] M(S s) => [s];")]
    public void AnyOtherCollectionExpressionIsOpaque(string members) =>
        Assert.Equal("CollectionExpression", Assert.Single(Opaques(Generic(Elements + members))).Reason);

    /// <summary>
    /// A class whose one <c>Add</c> does not take the elements' type adds them some other way, here through an extension
    /// method, and stays opaque.
    /// </summary>
    [Fact]
    public void AClassWhoseAddTakesAnotherTypeIsOpaque()
    {
        const string Source = """
            using System.Collections.Generic;
            class Texts : IEnumerable<int>
            {
                public void Add(string s) { }
                public IEnumerator<int> GetEnumerator() => null;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
            }
            static class Extensions { public static void Add(this Texts texts, int i) { } }
            class C { static Texts M(int a) => [a]; }
            """;

        Assert.Equal("CollectionExpression", Assert.Single(Opaques(Lowered.Source(Source))).Reason);
    }

    /// <summary>
    /// A framework with no <c>List&lt;T&gt;</c>, or whose <c>List&lt;T&gt;</c> has no parameterless constructor, has
    /// nothing to build an <c>IList&lt;T&gt;</c> from, and the collection expression stays opaque.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("public class List<T> : IList<T> { public List(int capacity) { } public void Add(T item) { } }")]
    public void AMutableInterfaceIsOpaqueWhereTheFrameworkHasNoListToBuild(string list)
    {
        string source = $$"""
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

            namespace System.Collections
            {
                public interface IEnumerable { }
            }

            namespace System.Collections.Generic
            {
                public interface IEnumerable<T> : IEnumerable { }
                public interface ICollection<T> : IEnumerable<T> { }
                public interface IList<T> : ICollection<T> { }
                {{list}}
            }

            class C
            {
                static System.Collections.Generic.IList<int> M(int a) => [a];
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "NoList",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        SyntaxNode syntax = compilation.GetTypeByMetadataName("C")!.GetMembers("M").Single().DeclaringSyntaxReferences[0].GetSyntax(TestContext.Current.CancellationToken);
        SemanticModel model = compilation.GetSemanticModel(syntax.SyntaxTree);

        IrProcedure procedure = IrLowerer.Lower((IMethodBodyOperation)model.GetOperation(syntax, TestContext.Current.CancellationToken)!, model, RenameMap.Empty, [], Runtimes.Migration);

        Assert.Empty(IrValidator.Validate(procedure));
        Assert.Contains(Opaques(procedure), static o => string.Equals(o.Reason, "CollectionExpression", StringComparison.Ordinal));
    }

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

    /// <summary>
    /// What the tests of P2-120 build from: two calls that yield an element, a class to initialize, and collection classes
    /// with one <c>Add</c> (their own, or inherited), with two, with one of another type, with a constructor that takes an
    /// argument, and a struct.
    /// </summary>
    private const string Elements = """
        static string F() => null; static string G() => null; static void Use(List<string> l) { }
        class X { public int P; }
        class Bag : IEnumerable<string> { public void Add(string s) { } public IEnumerator<string> GetEnumerator() => null; System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null; }
        class Derived : Bag { }
        class TwoAdds : Bag { public void Add(object o) { } }
        class Wide : IEnumerable<int> { public void Add(long l) { } public IEnumerator<int> GetEnumerator() => null; System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null; }
        class Sized : Bag { public Sized(int capacity = 0) { } }
        struct Pocket : IEnumerable<string> { public void Add(string s) { } public IEnumerator<string> GetEnumerator() => null; System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null; }

        """;

    /// <summary>
    /// The calls of <paramref name="procedure"/> in the order a depth-first walk from its entry meets them, which on a
    /// path with no branch is the order they run in: what is called, how many arguments it takes and what it yields.
    /// </summary>
    private static string[] Trace(IrProcedure procedure)
    {
        Dictionary<IrBlockId, IrBlock> blocks = procedure.Blocks.ToDictionary(static b => b.Id);
        List<string> calls = [];
        HashSet<IrBlockId> seen = [];
        Stack<IrBlockId> pending = new([procedure.Entry]);
        while (pending.TryPop(out IrBlockId? id))
        {
            if (!seen.Add(id))
            {
                continue;
            }

            IrBlock block = blocks[id];
            calls.AddRange(block.Instructions.OfType<IrCall>().Select(static c => $"{c.Callee.Value}/{c.Args.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{c.Target?.Type}"));
            IrBlockId[] successors = block.Terminator switch
            {
                IrGoto jump => [jump.Target],
                IrBranch branch => [branch.Then, branch.Else],
                _ => [],
            };
            foreach (IrBlockId successor in successors.Reverse())
            {
                pending.Push(successor);
            }
        }

        return [.. calls];
    }

    /// <summary>Lowers <c>M</c> of class <c>C</c> with <c>System.Collections.Generic</c> in scope.</summary>
    private static IrProcedure Generic(string members) =>
        Source($"using System;\nusing System.Collections.Generic;\nclass C\n{{\n{members}\n}}\n");
}
