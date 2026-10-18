using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>Tickets P2-242 and P2-246: which structs a driver reads field by field, and which of those it can also build.</summary>
public sealed class DriverStructTests
{
    private const string Source = """
        namespace N
        {
            public enum E { A }

            public struct Open { public float X; public int Y; public static int Shared; public const int Max = 1; }
            public struct Pair { public readonly float X, Y; public Pair(float x, float y) { X = x; Y = y; } public Pair(int n) { X = n; Y = n; } }
            public struct Mixed { public readonly string S; public readonly E K; internal Mixed(string s, E k) { S = s; K = k; } }
            public struct Sealed { public readonly int A; }
            public struct Counted { public readonly int A, B; public Counted(int a) { A = a; B = a; } }
            public struct ByRef { public readonly int A; public ByRef(ref int a) { A = a; } }
            public struct Typed { public readonly int A; public Typed(long a) { A = (int)a; } }
            public struct Named { public readonly int A; public Named(int b) { A = b; } }
            public struct Shy { public readonly int A; private Shy(int a) { A = a; } }
            public struct Hidden { private int a; public int A => a; }
            public struct Auto { public int A; public string B { get; set; } }
            public struct Point { public float X { get; } public float Y { get; } public Point(float x, float y) { X = x; Y = y; } }
            public struct Init { public int A { get; init; } public Init(int a) { A = a; } }
            public struct Guarded { public int A { get; private set; } }
            public struct Unread { public int A { private get; set; } }
            public struct Computed { public int A { get => field * 2; set; } }
            public struct WriteOnly { public int A { set => field = value; } }
            public struct Holder { public object O; }
            public struct Nested { public Open Inner; public Point At { get; set; } }
            public struct Middle { public Pair P { get; } public int N; public Middle(Pair p, int n) { P = p; N = n; } }
            public struct Deep { public Middle M; }
            public struct Wrapped { public Sealed S; }
            public struct Opaque { public Hidden H; }
            public struct Captured(int a) { public int B; public int A => a; }
            public struct Empty { }
            public struct G<T> { public T Value; }
            public ref struct Span { public int A; }
            public class Reference { public int A; }
            public class Outer { private struct Inner { public int A; } public static int M() => 0; }
            public static class Calls { public static int Read(Sealed s) => s.A; }
        }
        """;

    private static readonly Compilation Project = Compile(Source);

    private static DriverStruct? Of(string type) => DriverStruct.Of(Project.GetTypeByMetadataName(type)!);

    [Fact]
    public void AStructWithNoReadonlyField_IsBuiltByAnInitializer_IgnoringStaticAndConstFields()
    {
        DriverStruct flat = Of("N.Open")!;

        Assert.Equal(["X", "Y"], flat.Fields.Select(static f => f.Name), StringComparer.Ordinal);
        Assert.Null(flat.Constructor);
        Assert.True(flat.Buildable);
    }

    [Theory]
    [InlineData("N.Pair")]
    [InlineData("N.Mixed")]
    public void AReadonlyStruct_IsBuiltByTheConstructorThatTakesItsFields(string type)
    {
        DriverStruct flat = Of(type)!;

        Assert.Equal(flat.Fields.Select(static f => f.Name.ToUpperInvariant()), flat.Constructor!.Parameters.Select(static p => p.Name.ToUpperInvariant()), StringComparer.Ordinal);
        Assert.True(flat.Buildable);
    }

    /// <summary>No constructor takes exactly its fields (count, by-ref, type, name, visibility), so it is read but not built.</summary>
    [Theory]
    [InlineData("N.Sealed")]
    [InlineData("N.Counted")]
    [InlineData("N.ByRef")]
    [InlineData("N.Typed")]
    [InlineData("N.Named")]
    [InlineData("N.Shy")]
    public void AReadonlyStructWithoutAMatchingConstructor_IsReadButNotBuilt(string type)
    {
        DriverStruct flat = Of(type)!;

        Assert.Null(flat.Constructor);
        Assert.False(flat.Buildable);
    }

    /// <summary>Ticket P2-246: an auto-property is read by its own name, through its getter, in declaration order with the fields.</summary>
    [Fact]
    public void AnAutoPropertyWithASetter_IsReadAndBuiltByAnInitializer()
    {
        DriverStruct flat = Of("N.Auto")!;

        Assert.Equal(["A", "B"], flat.Fields.Select(static f => f.Name), StringComparer.Ordinal);
        Assert.Null(flat.Constructor);
        Assert.True(flat.Buildable);
    }

    /// <summary>Ticket P2-246: a get-only or <c>init</c> auto-property is built by the constructor that takes it by name.</summary>
    [Theory]
    [InlineData("N.Point", "X,Y")]
    [InlineData("N.Init", "A")]
    [InlineData("N.Middle", "P,N")]
    public void AnAutoPropertyWithoutAVisibleSetter_IsBuiltByItsConstructor(string type, string fields)
    {
        DriverStruct flat = Of(type)!;

        Assert.Equal(fields, string.Join(',', flat.Fields.Select(static f => f.Name)));
        Assert.Equal(fields, string.Join(',', flat.Constructor!.Parameters.Select(static p => p.Name.ToUpperInvariant())), StringComparer.OrdinalIgnoreCase);
        Assert.True(flat.Buildable);
    }

    /// <summary>Ticket P2-246: a field of a flat struct is read and built by the same rule, to a depth of two (<c>Deep</c> to <c>Middle</c> to <c>Pair</c>).</summary>
    [Theory]
    [InlineData("N.Nested", "Inner,At")]
    [InlineData("N.Deep", "M")]
    public void AFieldOfAFlatStruct_IsReadAndBuilt(string type, string fields)
    {
        DriverStruct flat = Of(type)!;

        Assert.Equal(fields, string.Join(',', flat.Fields.Select(static f => f.Name)));
        Assert.All(flat.Fields, static f => Assert.NotNull(DriverStruct.Of(f.Type)));
        Assert.Null(flat.Constructor);
        Assert.True(flat.Buildable);
    }

    /// <summary>Ticket P2-246: a private setter is not settable, and a nested struct that is not built leaves its owner read only.</summary>
    [Theory]
    [InlineData("N.Guarded")]
    [InlineData("N.Wrapped")]
    public void AStructWithAPartItCannotBuild_IsReadButNotBuilt(string type)
    {
        DriverStruct flat = Of(type)!;

        Assert.Null(flat.Constructor);
        Assert.False(flat.Buildable);
    }

    [Theory]
    [InlineData("N.Hidden")]
    [InlineData("N.Unread")]
    [InlineData("N.Computed")]
    [InlineData("N.WriteOnly")]
    [InlineData("N.Opaque")]
    [InlineData("N.Captured")]
    [InlineData("N.Holder")]
    [InlineData("N.Empty")]
    [InlineData("N.G`1")]
    [InlineData("N.Span")]
    [InlineData("N.Reference")]
    [InlineData("N.Outer+Inner")]
    [InlineData("N.E")]
    public void ATypeThatIsNotAFlatStruct_IsNone(string type) => Assert.Null(Of(type));

    [Theory]
    [InlineData(SpecialType.System_IntPtr)]
    [InlineData(SpecialType.System_DateTime)]
    public void AStructWithASpecialType_IsNone(SpecialType type) => Assert.Null(DriverStruct.Of(Project.GetSpecialType(type)));

    /// <summary>Ticket P2-246: a struct that contains itself (an error the compiler reports, but whose symbols exist) is none, not a loop.</summary>
    [Theory]
    [InlineData("N.Loop")]
    [InlineData("N.Left")]
    public void AStructInACycle_IsNone(string type)
    {
        Compilation cyclic = Compile("namespace N { public struct Loop { public Loop Self; } public struct Left { public Right R; } public struct Right { public Left L; } }");

        Assert.Null(DriverStruct.Of(cyclic.GetTypeByMetadataName(type)!));
    }

    /// <summary>
    /// Ticket P2-246: from metadata, which links no backing field to its property, a backing field is matched by the
    /// compiler's name for it and read only when its getter is compiler-generated; other private state is not flat.
    /// </summary>
    [Fact]
    public void AnAutoPropertyFromMetadata_IsReadByItsPropertysName()
    {
        DriverStruct flat = FromMetadata("L.Auto")!;

        Assert.Equal("X,N", string.Join(',', flat.Fields.Select(static f => f.Name)));
        Assert.NotNull(flat.Constructor);
        Assert.True(flat.Buildable);
    }

    [Theory]
    [InlineData("L.Computed")]
    [InlineData("L.Captured")]
    [InlineData("L.Hidden")]
    public void PrivateStateFromMetadataThatNoAutoPropertyGets_IsNone(string type) => Assert.Null(FromMetadata(type));

    /// <summary><paramref name="type"/> as a consumer of an emitted library sees it, through a metadata reference.</summary>
    private static DriverStruct? FromMetadata(string type)
    {
        Compilation library = Compile(
            """
            namespace L
            {
                public struct Auto { public float X { get; } public int N; public Auto(float x, int n) { X = x; N = n; } }
                public struct Computed { public int A { get => field + 1; set; } }
                public struct Captured(int a) { public int B; public int A => a; }
                public struct Hidden { private int a; public int A => a; }
            }
            """,
            "Library");
        using MemoryStream image = new();
        Assert.True(library.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
        Compilation consumer = Compile("namespace C { }", "Consumer", MetadataReference.CreateFromImage(image.ToArray()));
        return DriverStruct.Of(consumer.GetTypeByMetadataName(type)!);
    }

    [Fact]
    public void AnArray_IsNone() => Assert.Null(DriverStruct.Of(Project.CreateArrayTypeSymbol(Project.GetSpecialType(SpecialType.System_Int32))));

    /// <summary>A struct that is read but not built has no driver source as a parameter; <c>probe</c> refuses it before that.</summary>
    [Fact]
    public void AStructThatIsNotBuilt_HasNoDriverAsAParameter()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => DriverSource.Generate(Method(Project, "N.Calls", "Read")));

        Assert.Equal("no input can be built for N.Sealed", exception.Message);
    }
}
