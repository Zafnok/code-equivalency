using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-245 criterion 1: the source of a driver that reaches a member its friend cannot name (ADR 0062 decision 1),
/// bound by its exact signature through a typed delegate or <c>MethodInfo.Invoke</c>, both sides by one mechanism; each
/// shape compiles against the project it calls.
/// </summary>
public sealed class DriverReachTests
{
    private const string Source = """
        namespace N
        {
            public class C
            {
                private static int Count() => 0;
                private static void Touch() { }
                private void Note(int x) { }
                private static Secret Make(Secret s) => s;
                private static Secret[] Many(Secret[] s) => s;
                private static unsafe int Pointer(int* p) => 0;
                private static int Generic(System.Collections.Generic.List<Secret> s) => 0;
                private static int Grid(Secret[,] s) => 0;
                private static int Lists(System.Collections.Generic.List<Secret>[] s) => 0;
                public static int Wrapped(Outer o) => 0;
                private static int Wide(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q) => 0;
                public static int Open() => 0;
                public static int Dynamic(dynamic d) => 0;
                private class Secret { public int M() => 1; }
                private struct Value { private int Peek() => 0; }
            }

            public struct S { private int Peek() => 0; }
            public struct In { public float X; public int[] Data; }
            public struct Outer { public In Inner; public string Name; }
        }
        """;

    private static readonly CSharpCompilation Project = CSharpCompilation.Create(
        "Project",
        [CSharpSyntaxTree.ParseText(Source, cancellationToken: TestContext.Current.CancellationToken)],
        [.. Runtime, MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly.Location)],
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

    private static IMethodSymbol Member(string type, string name) => Project.GetTypeByMetadataName(type)!.GetMembers(name).OfType<IMethodSymbol>().Single();

    private static DriverSource.Shape Shape(DriverSource.Receiver receiver, DriverSource.Access access) =>
        new(receiver, access, new DriverHeap(), Formatter: false) { Builds = true };

    /// <summary>The driver's source, which must compile against <see cref="Project"/> as the driver would.</summary>
    private static string Driver(IMethodSymbol method, DriverSource.Shape shape)
    {
        string source = DriverSource.Generate(method, shape);
        CSharpCompilation driver = CSharpCompilation.Create(
            "EquivReplay",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [.. Project.References, Project.ToMetadataReference()],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        Diagnostic[] errors = [.. driver.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error)];
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(static e => e.ToString())));
        return source;
    }

    [Theory]
    [InlineData("N.C", "Count", "Delegate", "global::System.Func<int> f = (global::System.Func<int>)System.Delegate.CreateDelegate(typeof(global::System.Func<int>), H.Method(typeof(global::N.C), \"Count\", true, new System.Type[] {  }));", "r = f();")]
    [InlineData("N.C", "Touch", "Delegate", "global::System.Action f = (global::System.Action)System.Delegate.CreateDelegate(", "f();")]
    [InlineData("N.C", "Note", "Delegate", "global::System.Action<global::N.C, int> f =", "f(self, p0);")]
    [InlineData("N.C", "Note", "Invoke", "System.Reflection.MethodInfo m = H.Method(typeof(global::N.C), \"Note\", false, new System.Type[] { typeof(int) });", "H.Call(m, self, new object[] { p0 });")]
    [InlineData("N.C", "Make", "Invoke", "object p0 = V.Value(a[1], D.Ty[0]);", "r = H.Call(m, null, new object[] { p0 });")]
    [InlineData("N.C", "Many", "Invoke", "System.Type.GetType(\"N.C+Secret, Project, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null\", true).MakeArrayType()", "NotComparable(\"N.C.Secret[]\")")]
    [InlineData("N.C+Secret", "M", "Invoke", "object self = H.Construct(D.Ty[0]);", "r = (int)H.Call(m, self, new object[] {  });")]
    [InlineData("N.S", "Peek", "Invoke", "global::N.S self = new global::N.S();", "r = (int)H.Call(m, self, new object[] {  });")]
    public void AHiddenMember_IsBoundByItsExactSignature(string type, string member, string access, string declaration, string call)
    {
        string source = Driver(Member(type, member), Shape(DriverSource.Receiver.New, Enum.Parse<DriverSource.Access>(access)));

        Assert.Contains(declaration, source, StringComparison.Ordinal);
        Assert.Contains(call, source, StringComparison.Ordinal);
        Assert.Contains("internal static class H", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AHiddenResult_IsNotComparable()
    {
        string source = Driver(Member("N.C", "Make"), Shape(DriverSource.Receiver.New, DriverSource.Access.Invoke));

        Assert.Contains("object r;", source, StringComparison.Ordinal);
        Assert.Contains("return NotComparable(\"N.C.Secret\");", source, StringComparison.Ordinal);
    }

    /// <summary>A receiver from the wire is read by the value builder, which a driver created without a constructor by <c>FormatterServices</c> or <c>RuntimeHelpers</c>.</summary>
    [Theory]
    [InlineData(true, "System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type)")]
    [InlineData(false, "System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type)")]
    public void AReceiverFromTheWire_IsReadByTheValueBuilder(bool formatter, string uninitialized)
    {
        string source = DriverSource.Generate(Member("N.C+Secret", "M"), Shape(DriverSource.Receiver.Wire, DriverSource.Access.Invoke) with { Formatter = formatter });

        Assert.Contains("object self = V.Value(a[1], D.Ty[0]);", source, StringComparison.Ordinal);
        Assert.Contains(uninitialized, source, StringComparison.Ordinal);
    }

    /// <summary>Both sides by one mechanism: directly when a friend sees both, a delegate when both allow one, else <c>Invoke</c>.</summary>
    [Theory]
    [InlineData("N.C", "Open", "N.C", "Open", "Direct")]
    [InlineData("N.C", "Open", "N.C", "Count", "Delegate")]
    [InlineData("N.C", "Count", "N.C", "Open", "Delegate")]
    [InlineData("N.C", "Count", "N.C", "Make", "Invoke")]
    [InlineData("N.C", "Count", "N.C", "Wide", "Invoke")]
    [InlineData("N.C", "Count", "N.S", "Peek", "Invoke")]
    [InlineData("N.C", "Count", "N.C+Value", "Peek", "Invoke")]
    [InlineData("N.C", "Count", "N.C", "Dynamic", "Delegate")]
    public void BothSides_AreReachedByOneMechanism(string oldType, string oldMember, string newType, string newMember, string access) =>
        Assert.Equal(Enum.Parse<DriverSource.Access>(access), DriverSource.Reach(Member(oldType, oldMember), Member(newType, newMember)));

    /// <summary>A member no driver binds by its exact signature names the type it cannot spell.</summary>
    [Theory]
    [InlineData("Pointer", "N.C.Pointer(int*) is not bound by its exact signature: no driver can name int*")]
    [InlineData("Generic", "N.C.Generic(System.Collections.Generic.List<N.C.Secret>) is not bound by its exact signature: no driver can name System.Collections.Generic.List<N.C.Secret>")]
    [InlineData("Grid", "N.C.Grid(N.C.Secret[*,*]) is not bound by its exact signature: no driver can name N.C.Secret[*,*]")]
    [InlineData("Lists", "N.C.Lists(System.Collections.Generic.List<N.C.Secret>[]) is not bound by its exact signature: no driver can name System.Collections.Generic.List<N.C.Secret>[]")]
    [InlineData("Many", null)]
    [InlineData("Touch", null)]
    public void AMemberNoDriverBinds_IsNamed(string member, string? reason) => Assert.Equal(reason, DriverSource.Unbound(Member("N.C", member)));

    /// <summary>A struct nested in a struct is built by its own initializer, an array field by the value builder (tickets P2-246, P2-245).</summary>
    [Fact]
    public void ANestedStructWithAnArrayField_IsBuiltInSource() =>
        Assert.Contains(
            "global::N.Outer p0 = new global::N.Outer { Inner = new global::N.In { X = R.F(R.At(R.At(a[1], 0), 0)), Data = (int[])V.Value(R.At(R.At(a[1], 0), 1), typeof(int[])) }, Name = R.S(R.At(a[1], 1)) };",
            Driver(Member("N.C", "Wrapped"), Shape(DriverSource.Receiver.New, DriverSource.Access.Direct)),
            StringComparison.Ordinal);

    /// <summary>A driver whose inputs are no replay's or probe's has no value builder, so a reference-typed argument is only <c>null</c>, cast.</summary>
    [Fact]
    public void ADriverThatBuildsNothing_CastsAReferenceArgument() =>
        Assert.Contains("object p0 = (object)a[1];", DriverSource.Generate(Member("N.C", "Dynamic")).Replace("dynamic", "object", StringComparison.Ordinal), StringComparison.Ordinal);
}
