using System.Text.Json;

using Equiv.Core.Execution;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-245 criterion 4: <c>probe</c> takes a receiver by a constructor or by fields, and the argument kinds of ADR
/// 0062 decision 3 by name (decision 4), each saying how it was built (decision 5).
/// </summary>
public sealed class ProbeReachTests
{
    private const string Source = """
        namespace System.Windows.Forms { public interface IWin32Window { } }

        namespace N
        {
            public class Window : System.Windows.Forms.IWin32Window { }
            public interface IShape { }
            public abstract class Base
            {
                internal int Id;
                internal static int Id2;
                internal const int Id3 = 1;
                public int Read() => Id;
            }

            public class Account : Base, IShape
            {
                private readonly string owner;
                internal Window Host;
                internal int Score { get; set; }
                public Account(string owner) { this.owner = owner; }
                private Account() { }
                public int Name(string prefix) => 0;
            }

            public class Plain { public int M() => 0; }
            public struct Point { private int x; public int M() => x; }
            public struct Gen<T> { public T X; }
            public class G<T> { internal T X; }
            public delegate int Fields(int x);

            public class Kinds
            {
                public static int Take(object o, IShape s, Base b, Account a) => 0;
                public static int Arrays(int[] xs) => 0;
                public static int Token(System.Threading.CancellationToken t) => 0;
                public static int Delegates(System.Func<int, int> f, System.Action<int> a) => 0;
                public static int Structs(Point p, Gen<int> g, System.DateTime d) => 0;
                public static int Generic(G<int> g) => 0;
                public static int Funcs(Fields f) => 0;
                public static int Date(System.DateTime d) => 0;
                public static int Windows(Window[] ws) => 0;
                public static int Grid(int[,] g) => 0;
                private static int Hidden(Secret s, Hush h) => 0;
                public static int Framework(System.IO.Stream s, System.TimeSpan t, string? x) => 0;
                private class Secret { public Secret(int x) { } public Secret(string? a, string b) { } public unsafe Secret(int* p) { } }
                private delegate int Hush(int x);
            }
        }
        """;

    private static readonly Compilation Project = Compile(Source);

    private static ReplayArguments.SideCase Case(string type, string member, string arguments, string? receiver = null)
    {
        using JsonDocument json = JsonDocument.Parse(arguments);
        using JsonDocument? self = receiver is null ? null : JsonDocument.Parse(receiver);
        return ProbeArguments.Case(Method(Project, type, member), Project, [.. json.RootElement.EnumerateArray().Select(static e => e.Clone())], self?.RootElement.Clone());
    }

    private static (string Wire, string How)[] Built(ReplayArguments.SideCase built)
    {
        Assert.True(built.Input is not null, built.Reason);
        return [.. built.Arguments.Select(static a => (a.Built.Wire, a.Built.How))];
    }

    /// <summary>A receiver by a constructor, by fields of its own type or a base's, or by default: <c>new T()</c> beside the instance created without one.</summary>
    [Theory]
    [InlineData("N.Account", "Name", "{\"constructor\":\"string\",\"arguments\":[\"ada\"]}", "[\"@c\",\"1\",0,[\"ada\"]]", "constructor N.Account.Account(string)", "Wire")]
    [InlineData("N.Account", "Name", "{\"fields\":{\"owner\":\"ada\",\"Score\":2,\"Id\":3}}", "[\"@u\",\"1\",0,[[0,\"owner\",\"ada\"],[0,\"<Score>k__BackingField\",2],[1,\"Id\",3]]]", "uninitialized Account.owner, Account.Score, Base.Id", "Wire")]
    [InlineData("N.Plain", "M", null, "[\"@u\",\"1\",0,[]]", "uninitialized", "New")]
    [InlineData("N.Plain", "M", "null", "[\"@u\",\"1\",0,[]]", "uninitialized", "New")]
    [InlineData("N.Point", "M", null, "[\"@s\",0,[]]", "uninitialized", "New")]
    [InlineData("N.Point", "M", "{\"x\":4}", "[\"@s\",0,[[0,\"x\",4]]]", "uninitialized Point.x", "Wire")]
    public void AReceiver_IsBuiltAsTheAgentNamesIt(string type, string member, string? receiver, string wire, string how, string mode)
    {
        ReplayArguments.SideCase built = Case(type, member, string.Equals(type, "N.Account", StringComparison.Ordinal) ? "[\"x\"]" : "[]", receiver);

        Assert.True(built.Input is not null, built.Reason);
        Assert.Equal((wire, how), (built.Receiver!.Wire, built.Receiver.How));
        Assert.Equal(Enum.Parse<DriverSource.Receiver>(mode), built.Mode);
    }

    [Fact]
    public void AnAbstractReceiver_NeedsOneGiven() =>
        Assert.Equal("N.Base is abstract: give the receiver by a constructor or by its fields, of a type that derives from it", Case("N.Base", "Read", "[]").Reason);

    [Fact]
    public void AnAbstractReceiver_IsBuiltAsTheTypeItNames() =>
        Assert.Equal("[\"@u\",\"1\",1,[[0,\"Id\",5]]]", Case("N.Base", "Read", "[]", "{\"type\":\"N.Account\",\"fields\":{\"Id\":5}}").Receiver!.Wire);

    /// <summary>Arrays, tokens, delegates and objects by name, each with how it was built.</summary>
    [Theory]
    [InlineData("Arrays", "[[1,2]]", "[\"@a\",\"1\",0,[1,2]]", "value")]
    [InlineData("Arrays", "[null]", "null", "value")]
    [InlineData("Token", "[\"cancelled\"]", "[\"@t\",true]", "value")]
    [InlineData("Token", "[\"live\"]", "[\"@t\",false]", "value")]
    [InlineData("Take", "[{\"constructor\":\"\",\"arguments\":[]},{\"type\":\"N.Account\",\"fields\":{}},null,null]", "[\"@c\",\"1\",0,[]]", "constructor object.Object()")]
    public void AnArgumentKind_IsBuiltByName(string member, string arguments, string wire, string how) =>
        Assert.Equal((wire, how), Built(Case("N.Kinds", member, arguments))[0]);

    /// <summary>
    /// A constructor a friend cannot see is bound by its exact signature, a framework object is of the framework type
    /// <c>type</c> names, a struct is built by its constructor, and a constructor is named without nullable annotations.
    /// </summary>
    [Fact]
    public void Constructors_AreBoundWhereverTheyAre()
    {
        (string Wire, string How)[] hidden = Built(Case("N.Kinds", "Hidden", "[{\"constructor\":\"string, string\",\"arguments\":[\"a\",\"b\"]},null]"));
        (string Wire, string How)[] framework = Built(Case(
            "N.Kinds", "Framework", "[{\"type\":\"System.IO.MemoryStream\",\"constructor\":\"byte[]\",\"arguments\":[[1,2]]},{\"constructor\":\"long\",\"arguments\":[5]},null]"));

        Assert.Equal(("[\"@c\",\"1\",0,[\"a\",\"b\"]]", "constructor N.Kinds.Secret.Secret(string?, string)"), hidden[0]);
        Assert.Equal(("[\"@c\",\"2\",0,[[\"@a\",\"1\",0,[1,2]]]]", "constructor System.IO.MemoryStream.MemoryStream(byte[])"), framework[0]);
        Assert.Equal(("[\"@c\",\"3\",1,[5]]", "constructor System.TimeSpan.TimeSpan(long)"), framework[1]);
        Assert.Equal(
            ("[\"@c\",\"1\",0,[]]", "constructor N.Account.Account()"),
            Built(Case("N.Kinds", "Take", "[null,null,null,{\"constructor\":\"\",\"arguments\":[]}]"))[3]);
        Assert.Equal(
            "s: N.Kinds.Secret has no constructor (int*) a driver binds taking 1 argument(s)",
            Case("N.Kinds", "Hidden", "[{\"constructor\":\"int*\",\"arguments\":[1]},null]").Reason);
    }

    [Fact]
    public void Delegates_AnswerWithTheirValue() =>
        Assert.Equal(
            [("[\"@c\",\"1\",0,[5]]", "delegate"), ("[\"@c\",\"2\",1,[null]]", "delegate")],
            Built(Case("N.Kinds", "Delegates", "[{\"returns\":5},{\"returns\":null}]")));

    [Fact]
    public void AnInterfaceArgument_IsATypeThatImplementsIt() =>
        Assert.Equal(("[\"@u\",\"1\",0,[]]", "uninitialized"), Built(Case("N.Kinds", "Take", "[null,{\"type\":\"N.Account\",\"fields\":{}},null,null]"))[1]);

    /// <summary>Each JSON value that does not fit its parameter says why.</summary>
    [Theory]
    [InlineData("Arrays", "[{\"x\":1}]", "no int[] argument can be built for xs from {\"x\":1}")]
    [InlineData("Arrays", "[[1,\"a\"]]", "no int argument can be built for an element of xs from \"a\"")]
    [InlineData("Token", "[\"soon\"]", "t is a CancellationToken: \"cancelled\" or \"live\", not \"soon\"")]
    [InlineData("Token", "[1]", "t is a CancellationToken: \"cancelled\" or \"live\", not 1")]
    [InlineData("Delegates", "[{\"answer\":5},null]", "f is a delegate: {\"returns\": value}, not {\"answer\":5}")]
    [InlineData("Delegates", "[{\"returns\":5,\"x\":1},null]", "f is a delegate: {\"returns\": value}, not {\"returns\":5,\"x\":1}")]
    [InlineData("Delegates", "[{\"returns\":\"a\"},null]", "no int argument can be built for f's answer from \"a\"")]
    [InlineData("Delegates", "[null,{\"returns\":1}]", "a returns nothing, so its answer is null, not 1")]
    [InlineData("Delegates", "[5,null]", "no System.Func<int, int> argument can be built for f from 5")]
    [InlineData("Structs", "[null,{\"X\":1},{}]", "no N.Point argument can be built for p from null")]
    [InlineData("Structs", "[{\"x\":\"a\"},{\"X\":1},{}]", "no int argument can be built for p.x from \"a\"")]
    [InlineData("Structs", "[{\"y\":1},{\"X\":1},{}]", "p: N.Point has no field y")]
    [InlineData("Structs", "[{},{\"X\":1},{}]", "no N.Gen<int> argument can be built for g from {\"X\":1}")]
    [InlineData("Structs", "[{},5,{}]", "no N.Gen<int> argument can be built for g from 5")]
    [InlineData("Structs", "[{},null,{}]", "no N.Gen<int> argument can be built for g from null")]
    [InlineData("Generic", "[{\"fields\":{}}]", "g: N.G<int> is not a solution class an instance is created of without a constructor")]
    [InlineData("Funcs", "[{\"fields\":{}}]", "f is a delegate: {\"returns\": value}, not {\"fields\":{}}")]
    [InlineData("Take", "[5,null,null,null]", "no object argument can be built for o from 5")]
    [InlineData("Take", "[[1],null,null,null]", "no object argument can be built for o from [1]")]
    [InlineData("Date", "[{}]", "no System.DateTime argument can be built for d from {}")]
    [InlineData("Grid", "[[[1]]]", "no int[*,*] argument can be built for g from [[1]]")]
    [InlineData("Windows", "[[{\"fields\":{}}]]", "an element of ws: needs a live window handle or a message loop")]
    [InlineData("Take", "[{\"fields\":{\"x\":1}},null,null,null]", "o: object is not a solution class an instance is created of without a constructor")]
    [InlineData("Take", "[{},null,null,null]", "o is an object: null, {\"constructor\": \"<parameter types>\", \"arguments\": [...]} or {\"fields\": {...}}, not {}")]
    [InlineData("Take", "[{\"fields\":5},null,null,null]", "o is an object: null, {\"constructor\": \"<parameter types>\", \"arguments\": [...]} or {\"fields\": {...}}, not {\"fields\":5}")]
    [InlineData("Take", "[null,{\"type\":\"N.Gone\",\"fields\":{}},null,null]", "s: \"N.Gone\" is no N.IShape this side declares")]
    [InlineData("Take", "[null,{\"type\":5,\"fields\":{}},null,null]", "s: 5 is no N.IShape this side declares")]
    [InlineData("Take", "[null,{\"type\":\"N.Plain\",\"fields\":{}},null,null]", "s: \"N.Plain\" is no N.IShape this side declares")]
    [InlineData("Take", "[null,{\"type\":\"N.Window\",\"fields\":{}},null,null]", "s: \"N.Window\" is no N.IShape this side declares")]
    [InlineData("Take", "[{\"type\":\"N.Window\",\"fields\":{}},null,null,null]", "o: needs a live window handle or a message loop")]
    [InlineData("Take", "[null,null,{\"fields\":{}},null]", "b: N.Base is not a solution class an instance is created of without a constructor")]
    [InlineData("Take", "[null,null,{\"constructor\":\"\"},null]", "b: N.Base has no constructor () a driver binds taking 0 argument(s)")]
    [InlineData("Take", "[null,null,null,{\"constructor\":5}]", "a: N.Account has no constructor () a driver binds taking 0 argument(s)")]
    [InlineData("Take", "[null,null,null,{\"constructor\":\"string\"}]", "a: N.Account has no constructor (string) a driver binds taking 0 argument(s)")]
    [InlineData("Take", "[null,null,null,{\"constructor\":\"string\",\"arguments\":5}]", "a: N.Account has no constructor (string) a driver binds taking 0 argument(s)")]
    [InlineData("Take", "[null,null,null,{\"constructor\":\"string\",\"arguments\":[1]}]", "no string argument can be built for a's owner from 1")]
    [InlineData("Take", "[null,null,null,{\"fields\":{\"Host\":{\"fields\":{}}}}]", "a.Host: needs a live window handle or a message loop")]
    [InlineData("Take", "[null,null,null,{\"fields\":{\"Id2\":1}}]", "a: N.Account has no field Id2")]
    [InlineData("Take", "[null,null,null,{\"fields\":{\"Id3\":1}}]", "a: N.Account has no field Id3")]
    [InlineData("Hidden", "[null,{\"returns\":1}]", "no N.Kinds.Hush delegate can be built for h")]
    public void AValueThatDoesNotFit_IsNotConstructible(string member, string arguments, string reason) =>
        Assert.Equal(reason, Case("N.Kinds", member, arguments).Reason);
}
