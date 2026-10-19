using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-245 criteria 2 and 3: a replay's receiver and arguments built from the model's heap (ADR 0062 decisions 2 and
/// 3), as wire values with how each was built, and every state the drivers do not build named as the reason.
/// </summary>
public sealed class ModelObjectsTests
{
    private const string ThirdParty = """
        namespace ThirdParty
        {
            public struct Flat { public bool B; public float F; public double D; public decimal M; public int I; public string S; public int[] A; }
            public struct Nest { public Flat Inner; }
            public class Box { public int Size; }
            public class Locked { private Locked() { } }
        }
        """;

    private const string Source = """
        namespace System.Windows.Forms { public interface IWin32Window { } }

        namespace N
        {
            public class Window : System.Windows.Forms.IWin32Window { }
            public interface IThing { }
            public class Node
            {
                internal const int K = 1;
                internal Node Next;
                internal int Value;
                internal Window Owner;
                internal int Weight { get; set; }
                private Node() { }
                public int Peek() => Value;
            }

            public abstract class Shape { internal int Sides; }
            public class Square : Shape { internal int Side; }
            public class Thing : IThing { }
            public class G<T> { internal T Item; }
            public struct P { internal int X; internal string S; public int M() => X; }
            public struct Gen<T> { public T X; }
            public delegate int ByRef(ref int x);
            public class Outer { public class Nested : IThing { } }

            public class Kinds
            {
                private static int count;
                internal int Mine;
                public static int Objects(Node n, object o, IThing t, Shape s, Node m) => count;
                public static int Arrays(int[] a, int[] b, P[] ps, object[] os) => 0;
                public static int Structs(P p, ThirdParty.Flat f, ThirdParty.Nest n) => 0;
                public static int Others(Gen<int> g, System.DateTime d) => 0;
                public static int Delegates(System.Func<int, int> f, ByRef r) => 0;
                public static int Token(System.Threading.CancellationToken t) => 0;
                public static unsafe int Pointer(int* p) => 0;
                public static int Grid(int[,] g) => 0;
                public static int Generic(G<int> g) => 0;
                public static int Third(ThirdParty.Box b, ThirdParty.Locked l, System.Collections.Generic.List<int> list) => 0;
                public static int Scalars(int x, string s) => 0;
                public int Self(Kinds other) => Mine;
                private static int Hidden(Hush h) => 0;
                private delegate int Hush(int x);
            }

            public class Special : Kinds { }
        }
        """;

    private static readonly PortableExecutableReference ThirdPartyImage = Image(ThirdParty, "ThirdParty");

    private static readonly Compilation Project = Compile(Source, "Project", ThirdPartyImage, Compile("namespace Lib { public class Other { } }", "Lib").ToMetadataReference());

    private static readonly IrBoolValue True = new(Value: true);

    private static IrBitVecValue Bv(long value) => IrBitVecValue.FromSigned(32, value);

    private static IrSortValue S(string sort, long id) => new(sort, id);

    /// <summary>A map from <paramref name="key"/>'s sort, its default and its entries.</summary>
    private static IrMapValue Map(string key, IrValue @default, params (IrValue Key, IrValue Value)[] entries) =>
        new(new IrMap(new IrSort(key), @default.Type), @default, entries.ToImmutableDictionary(static e => e.Key, static e => e.Value));

    private static IrMapValue Elements(params IrValue[] items) =>
        new(new IrMap(new IrBitVec(32), items[0].Type), items[0], items.Select((v, i) => (Key: (IrValue)Bv(i), Value: v)).ToImmutableDictionary(static e => e.Key, static e => e.Value));

    private static PortableExecutableReference Image(string source, string name)
    {
        using MemoryStream image = new();
        Assert.True(Compile(source, name).Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static ReplayArguments.SideCase Case(string method, Dictionary<string, IrValue> values, Counterexample? model = null, string type = "N.Kinds")
    {
        IMethodSymbol symbol = Method(Project, type, method);
        string parameters = string.Join(", ", symbol.Parameters.Select(static p => $"%{p.Name}: bool"));
        return ReplayArguments.Case(symbol, Project, IrText.Parse($"proc \"X\" ({parameters}) entry B0 B0: ret"), values, ReplayArguments.Heap(values), model ?? Counterexample());
    }

    private static Dictionary<string, IrValue> Values(params (string Name, IrValue Value)[] values) => values.ToDictionary(static v => v.Name, static v => v.Value, StringComparer.Ordinal);

    private static (string Wire, string How)[] Built(ReplayArguments.SideCase built)
    {
        Assert.True(built.Input is not null, built.Reason);
        return [.. built.Arguments.Select(static a => (a.Built.Wire, a.Built.How))];
    }

    /// <summary>Objects, as <c>Objects</c>' five parameters: <paramref name="extra"/> added to a model each of whose objects is built.</summary>
    private static (string, IrValue)[] Objects(params (string, IrValue)[] extra) =>
    [
        ("n", S("N.Node", 1)), ("o", S("System.Object", 9)), ("t", S("N.IThing", 3)), ("s", S("N.Shape", 4)), ("m", S("N.Node", 1)),
        ("cast.N.IThing.N.Thing", Map("N.IThing", S("N.Thing", 6))), ("istype.N.IThing.N.Thing", Map("N.IThing", True)),
        ("cast.N.Shape.N.Square", Map("N.Shape", S("N.Square", 14))), ("istype.N.Shape.N.Square", Map("N.Shape", True)), .. extra,
    ];

    /// <summary>
    /// Objects: a cycle, a node shared by two parameters, an auto-property's backing field, a framework object, an interface
    /// whose type test names a nested class, and an abstract class whose downcast names its subclass, whose base field is
    /// read through the upcast's element.
    /// </summary>
    [Fact]
    public void Objects_AreTheModelsGraph()
    {
        IrSortValue node = S("N.Node", 1);
        Dictionary<string, IrValue> values = Values(
            ("n", node),
            ("o", S("System.Object", 9)),
            ("t", S("N.IThing", 2)),
            ("s", S("N.Shape", 3)),
            ("m", node),
            ("field.N.Node.Next", Map("N.Node", node)),
            ("field.N.Node.Value", Map("N.Node", Bv(7))),
            ("field.N.Node.Weight", Map("N.Node", Bv(2))),
            ("field.N.Square.Side", Map("N.Square", Bv(4))),
            ("field.N.Shape.Sides", Map("N.Shape", Bv(4))),
            ("cast.N.Shape.N.Square", Map("N.Shape", S("N.Square", 4))),
            ("istype.N.Shape.N.Square", Map("N.Shape", True)),
            ("cast.N.IThing.N.Outer_Nested", Map("N.IThing", S("N.Outer+Nested", 5))),
            ("istype.N.IThing.N.Outer_Nested", Map("N.IThing", True)));

        Assert.Equal(
            [
                ("[\"@u\",\"1\",0,[[0,\"Next\",[\"@r\",\"1\"]],[0,\"Value\",7],[0,\"<Weight>k__BackingField\",2]]]", "uninitialized Node.Next, Node.Value, Node.Weight"),
                ("[\"@c\",\"2\",0,[]]", "constructor object.Object()"),
                ("[\"@u\",\"3\",1,[]]", "uninitialized"),
                ("[\"@u\",\"4\",3,[[2,\"Sides\",4],[3,\"Side\",4]]]", "uninitialized Shape.Sides, Square.Side"),
                ("[\"@r\",\"1\"]", "uninitialized Node.Next, Node.Value, Node.Weight"),
            ],
            Built(Case("Objects", values)));
    }

    /// <summary>The model's state where no driver builds it is the reason, named.</summary>
    [Theory]
    [InlineData("field.N.Kinds.count", "N.Kinds")]
    [InlineData("field.N.Gone.x", "N.Gone")]
    [InlineData("field.N.G_1.Item", "N.G`1")]
    [InlineData("typeof.N.Node", null)]
    public void AStateNoDriverBuilds_IsNamed(string name, string? sort)
    {
        IrValue value = sort is null ? S("System.Type", 1) : Map(sort, Bv(0));

        Assert.Equal($"the model constrains {name}", Case("Scalars", Values(("x", Bv(1)), ("s", S("System.String", 1)), (name, value))).Reason);
    }

    /// <summary>Each object a driver cannot build says why.</summary>
    [Theory]
    [MemberData(nameof(Unbuildable))]
    public void AnObjectNoDriverBuilds_IsNotConstructible(string method, (string, IrValue)[] values, string reason) =>
        Assert.Equal(reason, Case(method, Values(values)).Reason);

    public static TheoryData<string, (string, IrValue)[], string> Unbuildable() => new()
    {
        { "Objects", Objects(("field.N.Node.Owner", Map("N.Node", S("N.Window", 5)))), "n's Node.Owner: needs a live window handle or a message loop" },
        { "Objects", Objects(("field.N.Node.Value", Map("N.Node", S("N.Node", 6)))), "no int argument can be built for n's Node.Value from the model" },
        { "Objects", [.. Objects().Skip(1), ("n", Bv(1))], "no N.Node argument can be built for n from the model" },
        { "Objects", [.. Objects().Skip(1)], "no N.Node argument can be built for n from the model" },
        { "Objects", [.. Objects().Where(static v => !v.Item1.StartsWith("istype.N.Shape", StringComparison.Ordinal))], "s is of N.Shape, which no instance is created of without a constructor" },
        { "Objects", [.. Objects().Where(static v => !v.Item1.Contains("N.Thing", StringComparison.Ordinal))], "t is of N.IThing, which no instance is created of without a constructor" },
        {
            "Objects",
            Objects(("cast.System.Object.N.Window", Map("System.Object", S("N.Window", 7))), ("istype.System.Object.N.Window", Map("System.Object", True))),
            "o: needs a live window handle or a message loop"
        },
        {
            "Objects",
            Objects(
                ("cast.System.Object.N.Node", Map("System.Object", S("N.Node", 7))),
                ("cast.System.Object.N.Thing", Map("System.Object", S("N.Thing", 18))),
                ("istype.System.Object.N.Node", Map("System.Object", True)),
                ("istype.System.Object.N.Thing", Map("System.Object", True))),
            "the model names no type o can be built as"
        },
        { "Objects", Objects(("istype.System.Object.System.Object", Map("System.Object", new IrBoolValue(Value: false)))), "the model names no type o can be built as" },
        {
            "Objects",
            Objects(("cast.N.Square.N.Shape", Map("N.Square", S("N.Shape", 1))), ("cast.N.IThing.N.Square", Map("N.IThing", S("N.Square", 8))), ("istype.N.IThing.N.Square", Map("N.IThing", True))),
            "the model's casts make two N.Square references one object"
        },
        { "Objects", Objects(("cast.N.Square.N.Gone", Map("N.Square", S("N.Gone", 1)))), "the model's cast.N.Square.N.Gone casts N.Square to N.Gone, which this side does not resolve" },
        {
            "Objects",
            Objects(("cast.System.Object.System.Collections.Generic.List_1", Map("System.Object", S("System.Collections.Generic.List`1", 7))), ("istype.System.Object.System.Collections.Generic.List_1", Map("System.Object", True))),
            "o is of System.Collections.Generic.List<T>, a type outside the solution with no public parameterless constructor the model leaves free"
        },
        { "Objects", Objects(("istype.N.IThing.N.Gone", Map("N.IThing", True))), "the model's istype.N.IThing.N.Gone tests a type this side does not resolve" },
        { "Objects", Objects(("istype.N.IThing.N.Gone_2", Map("N.IThing", True))), "the model's istype.N.IThing.N.Gone_2 tests a type this side does not resolve" },
        { "Objects", Objects(("istype.N.IThing.N.Gone_", Map("N.IThing", True))), "the model's istype.N.IThing.N.Gone_ tests a type this side does not resolve" },
        { "Grid", [("g", S("int[,]", 1))], "the model names no type g can be built as" },
        { "Generic", [("g", S("N.G`1", 1))], "g is of N.G<int>, which no instance is created of without a constructor" },
        {
            "Third",
            [("b", S("ThirdParty.Box", 1)), ("l", S("ThirdParty.Locked", 2)), ("list", S("System.Collections.Generic.List`1", 3)), ("field.ThirdParty.Box.Size", Map("ThirdParty.Box", Bv(1)))],
            "b is of ThirdParty.Box, a type outside the solution with no public parameterless constructor the model leaves free"
        },
        {
            "Third",
            [("b", S("ThirdParty.Box", 1)), ("l", S("ThirdParty.Locked", 2)), ("list", S("System.Collections.Generic.List`1", 3))],
            "l is of ThirdParty.Locked, a type outside the solution with no public parameterless constructor the model leaves free"
        },
        { "Pointer", [("p", S("int*", 1))], "no int* argument can be built for p from the model" },
        { "Scalars", [("s", S("System.String", 1))], "the model gives no value for x" },
        { "Scalars", [("x", S("S", 1)), ("s", S("System.String", 1))], "no int argument can be built for x from the model" },
        { "Others", [("g", S("N.Gen`1", 1)), ("d", S("System.DateTime", 2))], "no N.Gen<int> argument can be built for g from the model" },
        { "Others", [("g", Bv(1)), ("d", S("System.DateTime", 2))], "no N.Gen<int> argument can be built for g from the model" },
        { "Structs", [("p", S("N.P", 1)), ("f", S("ThirdParty.Flat", 4)), ("n", S("ThirdParty.Nest", 5)), ("field.N.P.X", Map("N.P", S("W", 1)))], "no int argument can be built for p's P.X from the model" },
        { "Structs", [("p", S("N.P", 1)), ("f", S("ThirdParty.Flat", 4)), ("n", S("ThirdParty.Nest", 5)), ("field.ThirdParty.Flat.I", Map("ThirdParty.Flat", S("W", 1)))], "no int argument can be built for f.I from the model" },
        { "Structs", [("p", S("N.P", 1)), ("f", S("ThirdParty.Flat", 4)), ("n", S("ThirdParty.Nest", 5)), ("null.N.P", Nulls("N.P", 1))], "the model's p is null" },
        { "Structs", [("p", S("N.P", 1)), ("f", S("ThirdParty.Flat", 4)), ("n", S("ThirdParty.Nest", 5))], "no default ThirdParty.Flat can be built for n.Inner" },
    };

    /// <summary>A framework object by its public parameterless constructor, when the model gives it no field; an upcast and boxing casts make no other object.</summary>
    [Fact]
    public void AFrameworkObject_IsBuiltByItsConstructor()
    {
        Dictionary<string, IrValue> values = Values(
            ("b", S("ThirdParty.Box", 1)),
            ("l", S("ThirdParty.Locked", 2)),
            ("list", S("System.Collections.Generic.List`1", 3)),
            ("null.ThirdParty.Locked", Nulls("ThirdParty.Locked", 2)),
            ("cast.System.Collections.Generic.List_1.System.Object", Map("System.Collections.Generic.List`1", S("System.Object", 4))),
            ("cast.int.System.Object", new IrMapValue(new IrMap(new IrBitVec(32), new IrSort("System.Object")), S("System.Object", 5), [])),
            ("cast.System.Collections.Generic.List_1.int", new IrMapValue(new IrMap(new IrSort("System.Collections.Generic.List`1"), new IrBitVec(32)), Bv(0), [])));

        Assert.Equal(
            [("[\"@c\",\"1\",0,[]]", "constructor ThirdParty.Box.Box()"), ("null", "value"), ("[\"@c\",\"2\",1,[]]", "constructor System.Collections.Generic.List<int>.List()")],
            Built(Case("Third", values)));
    }

    /// <summary>Arrays: from length and elements, one per model element, default elements without an element map.</summary>
    [Fact]
    public void Arrays_AreBuiltFromTheModel()
    {
        IrSortValue ints = S("int[]", 1);
        Dictionary<string, IrValue> values = Values(
            ("a", ints),
            ("b", ints),
            ("ps", S("N.P[]", 2)),
            ("os", S("object[]", 3)),
            ("length.int__", Map("int[]", Bv(2))),
            ("length.N.P__", Map("N.P[]", Bv(0))),
            ("array.object__", Map("object[]", Elements(S("System.Object", 9)))));

        Assert.Equal(
            [("[\"@a\",\"1\",0,[0,0]]", "value"), ("[\"@r\",\"1\"]", "value"), ("[\"@a\",\"2\",1,[]]", "value"), ("[\"@a\",\"3\",2,[]]", "value")],
            Built(Case("Arrays", values)));
    }

    [Theory]
    [InlineData("a", 70000, "the model's array a is longer than 65536")]
    [InlineData("ps", 1, "no default N.P can be built for an element of ps")]
    [InlineData("os", 1, "no object argument can be built for an element of os from the model")]
    public void AnArrayNoDriverBuilds_IsNotConstructible(string which, int length, string reason)
    {
        Dictionary<string, IrValue> values = Values(
            ("a", S("int[]", 1)),
            ("b", S("int[]", 1)),
            ("ps", S("N.P[]", 2)),
            ("os", S("object[]", 3)),
            ("length.int__", Map("int[]", Bv(string.Equals(which, "a", StringComparison.Ordinal) ? length : 0))),
            ("length.N.P__", Map("N.P[]", Bv(string.Equals(which, "ps", StringComparison.Ordinal) ? length : 0))),
            ("length.object__", Map("object[]", Bv(string.Equals(which, "os", StringComparison.Ordinal) ? length : 0))),
            ("array.object__", Map("object[]", Elements(Bv(1)))));

        Assert.Equal(reason, Case("Arrays", values).Reason);
    }

    /// <summary>Structs: a solution one created without a constructor, a third-party one from its fields or their defaults.</summary>
    [Fact]
    public void Structs_AreBuiltFieldByField()
    {
        Dictionary<string, IrValue> values = Values(
            ("p", S("N.P", 1)),
            ("f", S("ThirdParty.Flat", 4)),
            ("n", S("ThirdParty.Nest", 5)),
            ("field.N.P.X", Map("N.P", Bv(3))),
            ("field.ThirdParty.Flat.I", Map("ThirdParty.Flat", Bv(6))),
            ("field.ThirdParty.Nest.Inner", Map("ThirdParty.Nest", S("ThirdParty.Flat", 4))));

        Assert.Equal(
            [
                ("[\"@s\",0,[[0,\"X\",3]]]", "uninitialized P.X"),
                ("[false,\"0x00000000\",\"0x0000000000000000\",[0,0,0,0],6,null,null]", "value"),
                ("[[false,\"0x00000000\",\"0x0000000000000000\",[0,0,0,0],6,null,null]]", "value"),
            ],
            Built(Case("Structs", values)));
    }

    /// <summary>A delegate the model's runs never call throws if called; one they call is not built, nor one with a by-reference parameter.</summary>
    [Fact]
    public void Delegates_AreBuiltOnlyWhereTheModelNeverCallsThem()
    {
        IrSortValue f = S("System.Func`2", 1);
        Dictionary<string, IrValue> values = Values(("f", f), ("r", S("N.ByRef", 2)), ("null.N.ByRef", Nulls("N.ByRef", 2)));
        Counterexample called = new(new IrInputs([]), new IrRun(new IrThrew("A"), [], [new IrCallRecord(new CallIdentity("System.Func`2::Invoke(int)"), [S("Other", 1), f, Bv(2)])]), new IrRun(new IrThrew("B"), [], []));

        Assert.Equal([("[\"@c\",\"1\",0,[]]", "delegate"), ("null", "value")], Built(Case("Delegates", values)));
        Assert.Equal("the model's runs call f, and the counterexample holds none of the model's answers to that call", Case("Delegates", values, called).Reason);
        values.Remove("null.N.ByRef");
        Assert.Equal("no N.ByRef delegate can be built for r", Case("Delegates", values).Reason);
        Assert.Equal("no N.Kinds.Hush delegate can be built for h", Case("Hidden", Values(("h", S("N.Kinds+Hush", 1)))).Reason);
    }

    /// <summary>A receiver the model leaves free, of a type with no public parameterless constructor, is created without one.</summary>
    [Fact]
    public void AFreeReceiverWithNoPublicConstructor_IsCreatedWithoutOne()
    {
        ReplayArguments.SideCase built = Case("Peek", Values(("this", S("N.Node", 1))), type: "N.Node");

        Assert.Null(built.NewReceiver);
        Assert.Equal(DriverSource.Receiver.Wire, built.Mode);
        Assert.Equal(["[\"@u\",\"1\",0,[]]"], built.Input!.Arguments);
    }

    [Theory]
    [InlineData("System.OperationCanceledException", "[\"@t\",true]")]
    [InlineData("System.InvalidOperationException", "[\"@t\",false]")]
    public void AToken_IsCancelledWhenAModelRunEndsInCancellation(string exception, string wire)
    {
        Counterexample model = new(new IrInputs([]), new IrRun(new IrThrew(exception), [], []), new IrRun(new IrReturned(Value: null), [], []));

        Assert.Equal([(wire, "value")], Built(Case("Token", Values(("t", S("System.Threading.CancellationToken", 1))), model)));
    }

    /// <summary>The receiver is <c>new T()</c> only where the model leaves it free, on both sides.</summary>
    [Fact]
    public void TheReceiver_IsNewOnlyWhereTheModelLeavesItFree()
    {
        IrSortValue self = S("N.Kinds", 1);
        ReplayArguments.SideCase free = Case("Self", Values(("this", self), ("other", S("N.Kinds", 2))));
        ReplayArguments.SideCase given = Case("Self", Values(("this", self), ("other", S("N.Kinds", 2)), ("field.N.Kinds.Mine", Map("N.Kinds", Bv(1)))));
        ReplayArguments.SideCase shared = Case("Self", Values(("this", self), ("other", self)));
        ReplayArguments.SideCase derived = Case(
            "Self",
            Values(
                ("this", self),
                ("other", S("N.Kinds", 2)),
                ("cast.N.Kinds.N.Special", Map("N.Kinds", S("N.Special", 4), (self, S("N.Special", 3)))),
                ("istype.N.Kinds.N.Special", Map("N.Kinds", new IrBoolValue(Value: false), (self, True)))));
        ReplayArguments.SideCase @struct = Case("M", Values(("this", S("N.P", 1))), type: "N.P");
        ReplayArguments.SideCase structWithFields = Case("M", Values(("this", S("N.P", 1)), ("field.N.P.X", Map("N.P", Bv(1)))), type: "N.P");

        Assert.Equal(DriverSource.Receiver.New, free.Mode);
        Assert.Equal([new ReplayInput("this", "constructor N.Kinds.Kinds()"), new ReplayInput("other", "uninitialized")], free.Provenance(DriverSource.Receiver.New));
        Assert.Equal([new ReplayInput("this", "uninitialized"), new ReplayInput("other", "uninitialized")], free.Provenance(DriverSource.Receiver.Wire));
        Assert.Equal(DriverSource.Receiver.Wire, given.Mode);
        Assert.Equal(DriverSource.Receiver.Wire, shared.Mode);
        Assert.Equal(DriverSource.Receiver.Wire, derived.Mode);
        Assert.Equal("[\"@u\",\"1\",0,[]]", derived.Input!.Arguments[0]);
        Assert.Equal("N.Special", derived.Heap.Types[0].ToDisplayString());
        Assert.Equal(DriverSource.Receiver.New, @struct.Mode);
        Assert.Equal(DriverSource.Receiver.Wire, structWithFields.Mode);
        Assert.Equal(["[\"@s\",0,[[0,\"X\",1]]]"], structWithFields.Input!.Arguments);
        Assert.Equal(DriverSource.Receiver.Wire, ReplayArguments.Mode(free, given));
        Assert.Equal(DriverSource.Receiver.Wire, ReplayArguments.Mode(given, free));
        Assert.Equal(DriverSource.Receiver.New, ReplayArguments.Mode(free, @struct));
        Assert.Equal(["1", "\"s1\""], Case("Scalars", Values(("x", Bv(1)), ("s", S("System.String", 1)))).For(DriverSource.Receiver.Wire).Arguments);
    }

    /// <summary>A fresh array or collection the model makes an input's reference is a state no real run has.</summary>
    [Theory]
    [InlineData(1, "the model's new.int__ is an input's reference")]
    [InlineData(7, "")]
    public void AFreshReferenceThatIsAnInput_IsNotConstructible(int fresh, string reason) =>
        Assert.Equal(
            reason,
            Case("Arrays", Values(("a", S("int[]", 1)), ("b", S("int[]", 1)), ("ps", S("N.P[]", 2)), ("os", S("object[]", 3)), ("new.int__", Map("int[]", S("int[]", fresh))))).Reason);
}
