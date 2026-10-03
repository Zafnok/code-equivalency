using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// ADR 0043 (ticket P2-071): a use of a member in the effect-free catalogue is no call, in both lowerings. A catalogued
/// getter is the pure function <c>get:&lt;identity&gt;</c> of its null-checked receiver, a catalogued parameterless
/// constructor the next object of <c>new.&lt;Sort&gt;</c>, and a new <c>Collection&lt;T&gt;</c> converted to an interface
/// is a new <c>List&lt;T&gt;</c> there. Every other member stays a call.
/// </summary>
public sealed class EffectFreeMembersTests
{
    private const string Usings = "using System;\nusing System.Collections.Concurrent;\nusing System.Collections.Generic;\nusing System.Collections.ObjectModel;\n";

    public static TheoryData<string, string> Constructors => new()
    {
        { "List<int>", "System.Collections.Generic.List_1" },
        { "Dictionary<string, int>", "System.Collections.Generic.Dictionary_2" },
        { "HashSet<int>", "System.Collections.Generic.HashSet_1" },
        { "Queue<int>", "System.Collections.Generic.Queue_1" },
        { "Stack<int>", "System.Collections.Generic.Stack_1" },
        { "LinkedList<int>", "System.Collections.Generic.LinkedList_1" },
        { "SortedDictionary<string, int>", "System.Collections.Generic.SortedDictionary_2" },
        { "SortedList<string, int>", "System.Collections.Generic.SortedList_2" },
        { "SortedSet<int>", "System.Collections.Generic.SortedSet_1" },
        { "Collection<int>", "System.Collections.ObjectModel.Collection_1" },
        { "ConcurrentBag<int>", "System.Collections.Concurrent.ConcurrentBag_1" },
        { "ConcurrentDictionary<string, int>", "System.Collections.Concurrent.ConcurrentDictionary_2" },
        { "ConcurrentQueue<int>", "System.Collections.Concurrent.ConcurrentQueue_1" },
        { "ConcurrentStack<int>", "System.Collections.Concurrent.ConcurrentStack_1" },
    };

    /// <summary>The getter is one pure function of the receiver, raising nothing, behind the receiver's null check; no call is left.</summary>
    [Fact]
    public void AStringsLengthIsAPureFunctionOfTheStringAndNoCall()
    {
        foreach (IrProcedure procedure in Both("static int M(string s) { return s.Length; }"))
        {
            Assert.Empty(Lowered.Calls(procedure));
            IrPure length = Assert.Single(Instructions(procedure).OfType<IrPure>());
            Assert.Equal("get:System.String::get_Length()", length.Function);
            Assert.Empty(length.Throws);
            Assert.False(length.RuntimeSensitive);
            Assert.Equal(new IrSort("System.String"), Assert.Single(length.Args).Type);
            Assert.Equal(length.Target, Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrReturn>()).Value);
            Assert.Equal(["System.NullReferenceException"], procedure.Blocks.Select(static b => b.Terminator).OfType<IrThrow>().Select(static t => t.ExceptionType), StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The ticket's pair: the modern side's <c>_ = b.Length;</c> evaluates the read and stores nothing, so it adds a pure
    /// function and no call, and nothing is opaque.
    /// </summary>
    [Fact]
    public void ADiscardedLengthReadAddsNoCall()
    {
        IrProcedure procedure = Lowered.Method("static int M(string a, string b) { if (a == null || b == null) return 0; _ = b.Length; return a.Length; }");

        Assert.Empty(Lowered.Calls(procedure));
        Assert.Empty(Lowered.Opaques(procedure));
        Assert.Equal(["b", "a"], Instructions(procedure).OfType<IrPure>().Select(static p => p.Args[0].SourceName), StringComparer.Ordinal);
    }

    /// <summary>An assignment to a discard is its value, evaluated: a call on the right is still made.</summary>
    [Fact]
    public void AnAssignmentToADiscardEvaluatesItsValue()
    {
        IrProcedure procedure = Lowered.Method("static int F() { return 1; } static int M() { _ = F(); return 2; }");

        Assert.Empty(Lowered.Opaques(procedure));
        Assert.Equal("C::F()", Assert.Single(Lowered.Calls(procedure)).Callee.Value);
    }

    /// <summary>A catalogued constructor is no call: the new object is read from <c>new.&lt;Sort&gt;</c>, which is an input.</summary>
    [Theory]
    [MemberData(nameof(Constructors))]
    public void ACataloguedConstructorReadsTheNextNewObjectAndIsNoCall(string type, string sort)
    {
        foreach (IrProcedure procedure in Both($"static {type} M() {{ return new {type}(); }}"))
        {
            Assert.Empty(Lowered.Calls(procedure));
            IrParameter fresh = Assert.Single(procedure.Parameters, p => string.Equals(p.Var.Name, $"new.{sort}", StringComparison.Ordinal));
            Assert.Equal(IrParameterKind.In, fresh.Kind);
            IrMapRead read = Assert.Single(Instructions(procedure).OfType<IrMapRead>());
            Assert.Equal(fresh.Var, read.Map);
            Assert.Equal(read.Target, Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrReturn>()).Value);
            Assert.Empty(procedure.Blocks.Select(static b => b.Terminator).OfType<IrThrow>());
        }
    }

    /// <summary>Two creations of one sort are its first and second new object, as two array creations are.</summary>
    [Fact]
    public void TwoCreationsOfASortAreTwoObjects()
    {
        IrProcedure procedure = Lowered.Source(Usings + "class C { static List<int> M(bool first) { var a = new List<int>(); var b = new List<int>(); return first ? a : b; } }");

        Assert.Empty(Lowered.Calls(procedure));
        IrMapRead[] reads = [.. Instructions(procedure).OfType<IrMapRead>().Where(static r => r.Map.Name.StartsWith("new.", StringComparison.Ordinal))];
        Assert.Equal(2, reads.Length);
        Assert.NotEqual(reads[0].Key, reads[1].Key);
    }

    /// <summary>What the catalogue does not name stays a call: an argument, a member that reads the heap, a user's type or getter, a type of the same name declared in source.</summary>
    [Theory]
    [InlineData("class C { static List<int> M() { return new List<int>(4); } }", "System.Collections.Generic.List`1::.ctor(int)<int>")]
    [InlineData("class C { static int M(List<int> l) { return l.Count; } }", "System.Collections.Generic.List`1::get_Count()<int>")]
    [InlineData("class C { static C M() { return new C(); } }", "C::.ctor()")]
    [InlineData("class C { static System.Text.StringBuilder M() { return new System.Text.StringBuilder(); } }", "System.Text.StringBuilder::.ctor()")]
    [InlineData("class C { static IDisposable M() { return new System.IO.MemoryStream(); } }", "System.IO.MemoryStream::.ctor()")]
    [InlineData("class C { static IList<int> M(IList<int> l) { return new Collection<int>(l); } }", "System.Collections.ObjectModel.Collection`1::.ctor(System.Collections.Generic.IList<int>)<int>")]
    [InlineData("class C { int Length { get { return 1; } } static int M(C c) { return c.Length; } }", "C::get_Length()")]
    [InlineData("namespace System.Collections.Concurrent { public class ConcurrentBag<T> { } }\nclass C { static object M() { return new System.Collections.Concurrent.ConcurrentBag<int>(); } }", "System.Collections.Concurrent.ConcurrentBag`1::.ctor()<int>")]
    public void AMemberTheCatalogueDoesNotNameIsStillACall(string source, string callee)
    {
        Compilation compilation = RoslynTestCompilations.Compile(Usings + source);
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        foreach (IrProcedure procedure in new[] { IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration), IlLowerer.Lower(method, compilation, Runtimes.Migration) })
        {
            Assert.Empty(IrValidator.Validate(procedure));
            Assert.Equal(callee, Assert.Single(Lowered.Calls(procedure)).Callee.Value);
        }
    }

    /// <summary>The family rule: converted to an interface, a new <c>Collection&lt;T&gt;</c> lowers exactly as a new <c>List&lt;T&gt;</c> does.</summary>
    [Theory]
    [InlineData("IList<int>")]
    [InlineData("ICollection<int>")]
    [InlineData("IEnumerable<int>")]
    [InlineData("IReadOnlyList<int>")]
    [InlineData("System.Collections.IList")]
    public void ANewCollectionConvertedToAnInterfaceIsANewList(string @interface)
    {
        IrProcedure collection = Lowered.Source(Usings + $"class C {{ {@interface} f; void M() {{ this.f = new Collection<int>(); }} }}");
        IrProcedure list = Lowered.Source(Usings + $"class C {{ {@interface} f; void M() {{ this.f = new List<int>(); }} }}");

        Assert.Empty(Lowered.Calls(collection));
        Assert.Contains(collection.Parameters, static p => string.Equals(p.Var.Name, "new.System.Collections.Generic.List_1", StringComparison.Ordinal));
        Assert.Equal(IrText.Dump(list), IrText.Dump(collection));
    }

    /// <summary>Anywhere else a <c>Collection&lt;T&gt;</c> is its own sort: as itself, converted to <c>object</c>, or converted after it was stored.</summary>
    [Theory]
    [InlineData("static Collection<int> M() { return new Collection<int>(); }")]
    [InlineData("static object M() { return new Collection<int>(); }")]
    [InlineData("static IList<int> M() { var c = new Collection<int>(); return c; }")]
    public void ANewCollectionAnywhereElseIsItsOwnSort(string method)
    {
        IrProcedure procedure = Lowered.Source(Usings + $"class C {{ {method} }}");

        Assert.Empty(Lowered.Calls(procedure));
        Assert.Contains(procedure.Parameters, static p => string.Equals(p.Var.Name, "new.System.Collections.ObjectModel.Collection_1", StringComparison.Ordinal));
        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name.StartsWith("new.System.Collections.Generic", StringComparison.Ordinal));
    }

    /// <summary>Only <c>Collection&lt;T&gt;</c> is in <c>List&lt;T&gt;</c>'s family: any other catalogued collection converted to an interface is its own sort.</summary>
    [Fact]
    public void AnotherCollectionConvertedToAnInterfaceIsItsOwnSort()
    {
        IrProcedure procedure = Lowered.Source(Usings + "class C { static ICollection<int> M() { return new HashSet<int>(); } }");

        Assert.Empty(Lowered.Calls(procedure));
        Assert.Contains(procedure.Parameters, static p => string.Equals(p.Var.Name, "new.System.Collections.Generic.HashSet_1", StringComparison.Ordinal));
        Assert.Contains(procedure.Parameters, static p => string.Equals(p.Var.Name, "cast.System.Collections.Generic.HashSet_1.System.Collections.Generic.ICollection_1", StringComparison.Ordinal));
    }

    /// <summary>A catalogued constructor that a derived type's constructor calls on <c>this</c> allocates nothing, and stays a call.</summary>
    [Fact]
    public void ABaseConstructorCallIsStillACall()
    {
        Compilation compilation = RoslynTestCompilations.Compile(Usings + "class C : List<int> { public C() { } }");
        IMethodSymbol constructor = compilation.GetTypeByMetadataName("C")!.InstanceConstructors.Single();

        foreach (IrProcedure procedure in new[] { IrLowerer.Lower(constructor, compilation, RenameMap.Empty, [], Runtimes.Migration), IlLowerer.Lower(constructor, compilation, Runtimes.Migration) })
        {
            Assert.Empty(IrValidator.Validate(procedure));
            Assert.Equal("System.Collections.Generic.List`1::.ctor()<int>", Assert.Single(Lowered.Calls(procedure)).Callee.Value);
            Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name.StartsWith("new.", StringComparison.Ordinal));
        }
    }

    /// <summary>The IL lowering has no conversion at a <c>newobj</c>, so it does not apply the family rule (ADR 0043).</summary>
    [Fact]
    public void TheIlLoweringKeepsANewCollectionItsOwnSort()
    {
        IrProcedure procedure = Both("static IList<int> M() { return new Collection<int>(); }")[1];

        Assert.Empty(Lowered.Calls(procedure));
        Assert.Contains(procedure.Parameters, static p => string.Equals(p.Var.Name, "new.System.Collections.ObjectModel.Collection_1", StringComparison.Ordinal));
    }

    /// <summary>The catalogue itself: a getter's function is named for its identity, and only the listed members are in it.</summary>
    [Fact]
    public void TheCatalogueNamesAGettersFunctionByItsIdentity() =>
        Assert.Equal("get:System.String::get_Length()", EffectFreeMembers.Getter(new Equiv.Core.CallIdentity("System.String::get_Length()")));

    /// <summary>Method <c>M</c> of class <c>C</c>, lowered from IOperation and then from IL; both validate.</summary>
    private static IrProcedure[] Both(string method)
    {
        Compilation compilation = RoslynTestCompilations.Compile($"{Usings}class C\n{{\n{method}\n}}\n");
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol symbol = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure[] procedures = [IrLowerer.Lower(symbol, compilation, RenameMap.Empty, [], Runtimes.Migration), IlLowerer.Lower(symbol, compilation, Runtimes.Migration)];
        Assert.All(procedures, static p => Assert.Empty(IrValidator.Validate(p)));
        return procedures;
    }

    private static IEnumerable<IrInstruction> Instructions(IrProcedure procedure) => procedure.Blocks.SelectMany(static b => b.Instructions);
}
