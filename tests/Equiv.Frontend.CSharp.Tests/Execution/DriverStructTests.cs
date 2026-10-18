using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>Ticket P2-242: which structs a driver reads field by field, and which of those it can also build.</summary>
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
            public struct Auto { public int A { get; set; } }
            public struct Holder { public object O; }
            public struct Nested { public Open Inner; }
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

    [Theory]
    [InlineData("N.Hidden")]
    [InlineData("N.Auto")]
    [InlineData("N.Holder")]
    [InlineData("N.Nested")]
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
