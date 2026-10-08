using System.Collections.Immutable;

using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Frontend.CSharp.Tests.Fingerprinting;

/// <summary>Ticket M3-015 acceptance criteria 1 to 4: bound fingerprints and their runtime sensitivity (ADR 0024).</summary>
public sealed class BodyFingerprinterTests
{
    [Fact]
    public void LocalRenameKeepsTheFingerprint() =>
        Assert.Equal(
            Fingerprint("int M(int a) { int total = a + 1; return total * 2; }"),
            Fingerprint("int M(int a) { int sum = a + 1; return sum * 2; }"));

    [Fact]
    public void CommentsAndFormattingKeepTheFingerprint() =>
        Assert.Equal(
            Fingerprint("int M(int a) { return a + 1; }"),
            Fingerprint("""
                int M(int a)
                {
                    // One more than a.
                    return a+1; /* no change */
                }
                """));

    [Fact]
    public void ParameterRenameKeepsTheFingerprint() =>
        Assert.Equal(
            Fingerprint("int M(int a, int b) => a - b;"),
            Fingerprint("int M(int x, int y) => x - y;"));

    [Fact]
    public void SwappingParametersChangesTheFingerprint() =>
        Assert.NotEqual(
            Fingerprint("int M(int a, int b) => a - b;"),
            Fingerprint("int M(int a, int b) => b - a;"));

    [Fact]
    public void OverloadDriftChangesTheFingerprint()
    {
        const string Caller = "int M(int a) => F.G(a);";
        string legacy = Fingerprint(Caller, "public static class F { public static int G(object o) => 0; }").Sha256Hex;
        string modern = Fingerprint(Caller, "public static class F { public static int G(object o) => 0; public static int G(int i) => 0; }").Sha256Hex;

        Assert.NotEqual(legacy, modern, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("int M(int a) => a + 1;", "int M(int a) => a - 1;")]
    [InlineData("int M(int a) => a + 1;", "int M(int a) => a + 2;")]
    [InlineData("long M(int a) => a;", "long M(int a) => (long)(uint)a;")]
    [InlineData("string M() => \"a\";", "string M() => \"b\";")]
    [InlineData("int M(int a) => a++;", "int M(int a) => ++a;")]
    public void OperatorConstantAndConversionChangeTheFingerprint(string legacy, string modern) =>
        Assert.NotEqual(Fingerprint(legacy), Fingerprint(modern));

    [Fact]
    public void CheckedContextChangesTheFingerprint() =>
        Assert.NotEqual(
            Fingerprint("int M(int a) => a * 2;"),
            Fingerprint("int M(int a) => checked(a * 2);"));

    [Fact]
    public void InterpolatedStringHandlerChangesTheFingerprint()
    {
        const string Body = "string M(int a) => $\"a is {a}\";";
        BodyFingerprint formatted = Fingerprint(Compile(Body, LanguageVersion.CSharp9), legacy: true);
        BodyFingerprint handled = Fingerprint(Compile(Body, LanguageVersion.CSharp10), legacy: false);

        Assert.NotEqual(formatted, handled);
        Assert.Contains("string.Format", Text(Compile(Body, LanguageVersion.CSharp9)), StringComparison.Ordinal);
        Assert.Contains("DefaultInterpolatedStringHandler", Text(Compile(Body, LanguageVersion.CSharp10)), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTheHandlerTypeAnInterpolatedStringBindsStringFormat()
    {
        Compilation bare = CSharpCompilation.Create("Bare", [Parse("namespace N { public class C { public string M(int a) => $\"{a}\"; } }", LanguageVersion.CSharp14)]);

        Assert.Contains("string.Format", Text(bare), StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeChangedMemberIsRuntimeSensitive()
    {
        Assert.True(Fingerprint("int M(string s) => s.IndexOf(\"x\");").RuntimeSensitive);
        Assert.True(Fingerprint("System.Text.Encoding M() => System.Text.Encoding.Default;").RuntimeSensitive);
        Assert.False(Fingerprint("int M(string s) => s.Length;").RuntimeSensitive);
    }

    [Fact]
    public void ASuppressedRuntimeChangeIsNotRuntimeSensitive()
    {
        EquivConfig config = EquivConfig.Default with { SuppressRuntimeChanges = ["System.String::IndexOf("] };
        Compilation compilation = Compile("int M(string s) => s.IndexOf(\"x\");");

        Assert.False(BodyFingerprinter.Compute(Method(compilation), compilation, config, [], Runtimes.Migration)!.RuntimeSensitive);
    }

    [Theory]
    [InlineData("int M(double d) => (int)d;")]
    [InlineData("long? M(float? f) => (long?)f;")]
    [InlineData("System.DayOfWeek M(double d) => (System.DayOfWeek)d;")]
    public void FloatToIntConversionIsRuntimeSensitive(string member) =>
        Assert.True(Fingerprint(member).RuntimeSensitive);

    [Theory]
    [InlineData("double M(int i) => i;")]
    [InlineData("int M(decimal m) => (int)m;")]
    [InlineData("int M(long l) => (int)l;")]
    [InlineData("object M(int[] a) => a;")]
    public void OtherConversionsAreNotRuntimeSensitive(string member) =>
        Assert.False(Fingerprint(member).RuntimeSensitive);

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void FloatOnASideWhoseX87FlagIsSetIsRuntimeSensitive(bool x87, bool expected)
    {
        Compilation compilation = Compile("double M(double d) => d * 2;");

        Assert.Equal(
            expected,
            BodyFingerprinter.Compute(Method(compilation), compilation, EquivConfig.Default, legacy: true, Runtimes.Between("net48", "net10.0", x87))!.RuntimeSensitive);
    }

    [Fact]
    public void AnAutoAccessorIsFingerprintedAsTheBodyTheCompilerGenerates()
    {
        Compilation compilation = Compile("public int P { get; set; } public int Q { get; init; }");

        string text = Text(compilation, "get_P");

        Assert.StartsWith("PropertyGet static=False async=False returns=System.Int32 ()\nAutoAccessor init=False\n", text, StringComparison.Ordinal);
        Assert.Contains("AutoAccessor init=True", Text(compilation, "set_Q"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("partial void M();", "M")]
    [InlineData("public abstract int P { get; }", "get_P")]
    public void ADeclarationWithoutABodyHasNoFingerprint(string member, string name)
    {
        Compilation compilation = Compile(member, partial: true);

        Assert.Null(BodyFingerprinter.Compute(Method(compilation, name), compilation, EquivConfig.Default, legacy: false, Runtimes.Migration));
    }

    /// <summary>Ticket P2-107: on one runtime a partial method's code, which its implementing part holds, is what is fingerprinted.</summary>
    [Fact]
    public void APartialMethodIsFingerprintedByItsImplementingPartOnOneRuntime()
    {
        const string Definition = "private static partial int M(int a);";
        Compilation compilation = Compile(Definition + " private static partial int M(int a) { int b = a + 1; return b; }", partial: true);

        string text = BodyFingerprinter.Text(Method(compilation), compilation, EquivConfig.Default, [], OneRuntime).Text!;

        Assert.StartsWith("Ordinary static=True async=False returns=System.Int32 (None System.Int32)\nImplementingPart\nMethodBody", text, StringComparison.Ordinal);
        Assert.Contains("symbols=P0", text, StringComparison.Ordinal);
        Assert.Equal(OnOneRuntime(compilation), OnOneRuntime(Compile(Definition + " private static partial int M(int x) { int y = x + 1; return y; }", partial: true)));
        Assert.NotEqual(OnOneRuntime(compilation), OnOneRuntime(Compile(Definition + " private static partial int M(int a) { int b = a + 2; return b; }", partial: true)));
        Assert.NotEqual(OnOneRuntime(compilation), OnOneRuntime(Compile("private static int M(int a) { int b = a + 1; return b; }", partial: true)));
    }

    /// <summary>Ticket P2-107 criterion 3: a pair that crosses a runtime keeps what it has, which is no fingerprint.</summary>
    [Theory]
    [InlineData("net48", "net10.0")]
    [InlineData("net8.0", "net9.0")]
    public void APartialMethodHasNoFingerprintOnAPairThatCrossesARuntime(string legacy, string modern)
    {
        Compilation compilation = Compile("private static partial int M(int a); private static partial int M(int a) { return a + 1; }", partial: true);

        Assert.Null(BodyFingerprinter.Compute(Method(compilation), compilation, EquivConfig.Default, legacy: false, Runtimes.Between(legacy, modern)));
    }

    /// <summary>
    /// Ticket P2-107: the methods on one runtime that still have no fingerprint. A partial method with no implementing part, one
    /// whose implementing part is <c>extern</c> or does not bind, an abstract accessor, and a partial member that is not an
    /// ordinary method: a partial property's accessor, and a partial constructor, which runs its type's initializers too.
    /// </summary>
    [Theory]
    [InlineData("partial void M();", "M")]
    [InlineData("private static partial int M(); [System.Runtime.InteropServices.DllImport(\"a.dll\")] private static extern partial int M();", "M")]
    [InlineData("private static partial int M(int a); private static partial int M(int a) { return Missing(a); }", "M")]
    [InlineData("private static partial int M(int a); private static partial int M(int a) { return a + ; }", "M")]
    [InlineData("public abstract int P { get; }", "get_P")]
    [InlineData("public partial int Q { get; } public partial int Q { get => 1; }", "get_Q")]
    [InlineData("public partial C(); public partial C() { }", ".ctor")]
    public void ADeclarationWhoseCodeIsNotABoundBodyHasNoFingerprintOnOneRuntime(string member, string name)
    {
        Compilation compilation = Compile(member, partial: true);

        Assert.Null(BodyFingerprinter.Compute(Method(compilation, name), compilation, EquivConfig.Default, legacy: false, OneRuntime));
    }

    /// <summary>Ticket P2-107: the compiler writes a record's primary constructor from the type's declarations, and no declaration holds it.</summary>
    [Fact]
    public void ARecordsPrimaryConstructorHasNoFingerprintOnOneRuntime()
    {
        Compilation compilation = Compile("public record R(int X);", wrap: false);
        IMethodSymbol constructor = compilation.GetTypeByMetadataName("N.R")!.InstanceConstructors.Single(static c => c.Parameters is [{ Name: "X" }]);

        Assert.Null(BodyFingerprinter.Compute(constructor, compilation, EquivConfig.Default, legacy: false, OneRuntime));
    }

    /// <summary>
    /// Ticket P2-107: an <c>extern</c> local function has no bound code, so what it calls is in its attributes. This is the shape
    /// the interop generator gives a <c>[LibraryImport]</c> method.
    /// </summary>
    [Theory]
    [InlineData("\"user32.dll\", EntryPoint = \"Beep\"", "int", "int")]
    [InlineData("\"kernel32.dll\", EntryPoint = \"Boop\"", "int", "int")]
    [InlineData("\"kernel32.dll\", EntryPoint = \"Beep\", SetLastError = true", "int", "int")]
    [InlineData("\"kernel32.dll\", EntryPoint = \"Beep\"", "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] int", "int")]
    [InlineData("\"kernel32.dll\", EntryPoint = \"Beep\"", "int", "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] int")]
    public void AnExternLocalFunctionIsFingerprintedByItsAttributes(string import, string returns, string parameter)
    {
        Compilation baseline = Compile(Stub("\"kernel32.dll\", EntryPoint = \"Beep\"", "int", "int"), partial: true);

        Assert.NotEqual(OnOneRuntime(baseline), OnOneRuntime(Compile(Stub(import, returns, parameter), partial: true)));
        Assert.Equal(OnOneRuntime(baseline), OnOneRuntime(Compile("private const string Library = \"kernel32.dll\"; " + Stub("Library, EntryPoint = \"Beep\"", "int", "int"), partial: true)));
    }

    /// <summary>Ticket P2-107: the attributes of both parts are in the text once, each as its constructor and its bound arguments.</summary>
    [Fact]
    public void APartialMethodsAttributesAreInItsFingerprint()
    {
        const string Implementation = "[System.Diagnostics.DebuggerStepThrough] private static partial int M(int a) { return a; }";
        Compilation compilation = Compile("[System.Obsolete(\"old\")] private static partial int M([System.ComponentModel.DefaultValue(1)] int a); " + Implementation, partial: true);

        string text = BodyFingerprinter.Text(Method(compilation), compilation, EquivConfig.Default, [], OneRuntime).Text!;

        Assert.Contains("Attribute method: System.ObsoleteAttribute.ObsoleteAttribute(string?) = System.ObsoleteAttribute(\"old\")\n", text, StringComparison.Ordinal);
        Assert.Contains("Attribute method #0: System.ComponentModel.DefaultValueAttribute.DefaultValueAttribute(int) = System.ComponentModel.DefaultValueAttribute(1)\n", text, StringComparison.Ordinal);
        Assert.Contains("Attribute method: System.Diagnostics.DebuggerStepThroughAttribute.DebuggerStepThroughAttribute() = System.Diagnostics.DebuggerStepThroughAttribute\n", text, StringComparison.Ordinal);
        Assert.NotEqual(
            OnOneRuntime(compilation),
            OnOneRuntime(Compile("[System.Obsolete(\"new\")] private static partial int M([System.ComponentModel.DefaultValue(1)] int a); " + Implementation, partial: true)));
    }

    [Fact]
    public void AConstructorRunsItsInitializersUnlessItChainsToThis()
    {
        const string Members = "private int f = 1; private static int s = 2; public int P { get; } = 3; public int Q { get; set; } private int g; public C() { } public C(int x) : this() { } static C() { }";
        Compilation compilation = Compile(Members);
        IMethodSymbol[] constructors = [.. compilation.GetTypeByMetadataName("N.C")!.Constructors];

        string instance = Text(compilation, constructors.Single(static c => !c.IsStatic && c.Parameters.IsEmpty));
        string chained = Text(compilation, constructors.Single(static c => c.Parameters.Length == 1));
        string type = Text(compilation, constructors.Single(static c => c.IsStatic));

        Assert.Contains("FieldInitializer", instance, StringComparison.Ordinal);
        Assert.Contains("PropertyInitializer", instance, StringComparison.Ordinal);
        Assert.DoesNotContain("Initializer syntax=EqualsValueClause", chained, StringComparison.Ordinal);
        Assert.Contains("symbols=N.C::s", type, StringComparison.Ordinal);
        Assert.DoesNotContain("N.C::f", type, StringComparison.Ordinal);
    }

    [Fact]
    public void RenamesApplyToTypesAndCallees()
    {
        RenameMap renames = new(ImmutableDictionary<string, string>.Empty.Add("Old", "N"), []);
        const string Legacy = "namespace Old { public static class F { public static int G(int i) => i; } } namespace N { public class C { public Old.F[] M(int a) { Old.F.G(a); return null; } } }";
        const string Modern = "namespace N { public static class F { public static int G(int i) => i; } } namespace N { public class C { public N.F[] M(int a) { N.F.G(a); return null; } } }";
        EquivConfig config = EquivConfig.Default with { Renames = renames };

        Compilation legacy = RoslynTestCompilations.Compile(Legacy);
        Compilation modern = RoslynTestCompilations.Compile(Modern);

        Assert.Equal(
            BodyFingerprinter.Compute(Method(legacy), legacy, config, [], Runtimes.Migration),
            BodyFingerprinter.Compute(Method(modern), modern, config, [], Runtimes.Migration));
    }

    [Fact]
    public void ALegacyCatalogueEntryThatPassesArgumentsThroughRewritesTheCallee()
    {
        const string Legacy = "namespace Old { public class R { } public class B { public R Ok() => null; } } namespace N { public class C : Old.B { public Old.R M() => Ok(); } }";
        const string Modern = "namespace New { public class R { } public class B { public R Ok() => null; } } namespace N { public class C : New.B { public New.R M() => Ok(); } }";
        ImmutableArray<ApiEquivalence> entries =
        [
            new("ok", IsType: false, "Old.B::Ok()", "New.B::Ok()", [new ApiArgument(0)], "r", new Uri("https://learn.microsoft.com/")),
            new("r", IsType: true, "Old.R", "New.R", [], "r", new Uri("https://learn.microsoft.com/")),
            new("b", IsType: true, "Old.B", "New.B", [], "r", new Uri("https://learn.microsoft.com/")),
        ];
        ImmutableArray<ApiEquivalence> reordering = [entries[0] with { Arguments = [new ApiArgument(0), new ApiArgument(Source: null, ConstantType: "bool", Constant: "true")] }, entries[1], entries[2]];
        Compilation legacy = RoslynTestCompilations.Compile(Legacy);
        Compilation modern = RoslynTestCompilations.Compile(Modern);
        BodyFingerprint? modernPrint = BodyFingerprinter.Compute(Method(modern), modern, EquivConfig.Default, [], Runtimes.Migration);

        Assert.Equal(modernPrint, BodyFingerprinter.Compute(Method(legacy), legacy, EquivConfig.Default, entries, Runtimes.Migration));
        Assert.NotEqual(modernPrint, BodyFingerprinter.Compute(Method(legacy), legacy, EquivConfig.Default, reordering, Runtimes.Migration));
        Assert.NotEqual(modernPrint, BodyFingerprinter.Compute(Method(legacy), legacy, EquivConfig.Default, legacy: true, Runtimes.Migration));
    }

    [Fact]
    public void AnErroneousBodyIsStillSerialised()
    {
        Compilation compilation = Compile("int M(int a) => Missing(a) + a;");

        Assert.Contains("Invalid", Text(compilation), StringComparison.Ordinal);
    }

    [Fact]
    public Task StraightLineSerialisation() =>
        Verify(Text(Compile("""
            int M(int a, long b, bool c)
            {
                int d = a * 3;
                long e = checked(b + d);
                if (c && e > 10) { return d; }
                return (int)e;
            }
            """)));

    [Fact]
    public Task SymbolsSerialisation() =>
        Verify(Text(Compile("""
            private int field;
            private event System.EventHandler Changed;
            private int[,] grid = new int[2, 2];
            public int this[int i] { get => i; }
            public int Set { set { field = value; } }
            public static implicit operator C(int i) => new C();
            public System.Collections.Generic.List<string> M<T>(object o, decimal m, string s, dynamic d, T t, System.Collections.Generic.List<int> list, ref int r)
            {
                int[] numbers = new[] { 1, 2 };
                System.Func<int, int> twice = x => x * 2;
                int Local<U>(U u) => typeof(U).Name.Length;
                decimal n = -m;
                n += m;
                n++;
                C converted = 5;
                char ch = 'x';
                string nothing = null;
                long? lifted = (int?)null;
                System.DayOfWeek day = System.DayOfWeek.Monday;
                bool tests = o is string text && text.Length > 1 && o is C { field: > 0 } && numbers is [1, ..] && o is not null && o is int;
                this.Changed += (sender, args) => { };
                System.Func<string> bound = s.ToString;
                System.TypedReference reference = __makeref(r);
                d.Frob(1);
                Set = this[0] + field + grid[0, 0] + sizeof(int) + ch + Local(t) + Local(1) + twice(r);
                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                builder.Append($"{m}");
                if (o is string) { goto done; }
                try { r = s.Length; } catch (System.InvalidOperationException) { throw; }
                done:
                return new System.Collections.Generic.List<string> { s, $"{n} {tests} {lifted} {day} {nothing}" };
            }
            """)));

    [Fact]
    public Task ConstructorSerialisation()
    {
        Compilation compilation = Compile("""
            public record R(int X);
            public class C
            {
                private readonly int seed = 7;
                public string Name { get; } = "n";
                public C(R r) { R copy = r with { X = seed }; }
            }
            """, wrap: false);

        return Verify(Text(compilation, ".ctor"));
    }

    /// <summary>A side of a pair whose two projects run on one runtime, so the pair crosses none (ADR 0040 decision 2).</summary>
    private static SideRuntime OneRuntime => Runtimes.Between("net8.0", "net8.0");

    private static BodyFingerprint? OnOneRuntime(Compilation compilation) =>
        BodyFingerprinter.Compute(Method(compilation), compilation, EquivConfig.Default, legacy: false, OneRuntime);

    /// <summary>A partial method and an implementing part that calls a local <c>extern</c> function, as the interop generator writes one.</summary>
    private static string Stub(string import, string returns, string parameter) =>
        $"private static partial bool M(int a); private static partial bool M(int a) {{ return __PInvoke(a) != 0; [System.Runtime.InteropServices.DllImport({import})] static extern {returns} __PInvoke({parameter} x); }}";

    private static BodyFingerprint Fingerprint(string member, string extra = "") => Fingerprint(Compile(member, extra: extra), legacy: false);

    private static BodyFingerprint Fingerprint(Compilation compilation, bool legacy) =>
        BodyFingerprinter.Compute(Method(compilation), compilation, EquivConfig.Default, legacy, Runtimes.Migration)!;

    private static string Text(Compilation compilation, string name = "M") => Text(compilation, Method(compilation, name));

    private static string Text(Compilation compilation, IMethodSymbol method) =>
        BodyFingerprinter.Text(method, compilation, EquivConfig.Default, [], Runtimes.Migration).Text!;

    private static IMethodSymbol Method(Compilation compilation, string name = "M") =>
        compilation.GetTypeByMetadataName("N.C")!.GetMembers(name).OfType<IMethodSymbol>().First();

    private static CSharpCompilation Compile(
        string members, LanguageVersion version = LanguageVersion.Preview, Platform platform = Platform.AnyCpu, string extra = "", bool partial = false, bool wrap = true)
    {
        string source = wrap ? $"namespace N {{ {extra} public {(partial ? "abstract partial " : string.Empty)}class C {{ {members} }} }}" : $"namespace N {{ {members} }}";
        return CSharpCompilation.Create(
            "Snippet",
            [Parse(source, version)],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, platform: platform, allowUnsafe: true));
    }

    private static SyntaxTree Parse(string source, LanguageVersion version) =>
        CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(version), cancellationToken: TestContext.Current.CancellationToken);
}
