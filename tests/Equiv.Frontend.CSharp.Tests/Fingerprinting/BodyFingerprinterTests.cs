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

    /// <summary>
    /// Ticket P2-144: a <c>switch</c> expression that may match no arm ends in a throw the bound tree does not hold, of
    /// <c>SwitchExpressionException</c> where the reference assemblies have the type. The text names its constructor, and
    /// the body is runtime-sensitive on a pair that crosses .NET Core 3.0, where the type first shipped.
    /// </summary>
    [Theory]
    [InlineData("net48", "net10.0", true)]
    [InlineData("netcoreapp2.1", "netcoreapp3.1", true)]
    [InlineData("netcoreapp3.1", "net10.0", false)]
    [InlineData("net10.0", "net10.0", false)]
    public void ASwitchExpressionThatMayMatchNoArmNamesItsNoMatchConstructor(string legacy, string modern, bool sensitive)
    {
        Compilation compilation = Compile(OpenSwitch);
        (string? text, bool runtimeSensitive) = BodyFingerprinter.Text(Method(compilation), compilation, EquivConfig.Default, [], Runtimes.Between(legacy, modern));

        Assert.Contains($"SwitchExpression syntax=SwitchExpression implicit=False type=int context={NoMatchConstructor}\n", text!.Replace("System.Int32", "int", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(sensitive, runtimeSensitive);
    }

    [Fact]
    public void ASuppressedNoMatchRowIsNotRuntimeSensitive()
    {
        Compilation compilation = Compile(OpenSwitch);
        EquivConfig config = EquivConfig.Default with { SuppressRuntimeChanges = ["System.Runtime.CompilerServices.SwitchExpressionException::.ctor("] };

        Assert.False(BodyFingerprinter.Compute(Method(compilation), compilation, config, [], Runtimes.Migration)!.RuntimeSensitive);
    }

    /// <summary>
    /// Where the reference assemblies have no <c>SwitchExpressionException</c> the same text throws
    /// <c>InvalidOperationException</c>, which no row flags: the two sides' fingerprints differ, so the pair is not congruent
    /// (criterion 3), and two sides that both lack the type are the same body.
    /// </summary>
    [Fact]
    public void WithoutTheNoMatchTypeTheSameTextHasAnotherFingerprint()
    {
        const string Framework = "namespace System { public class InvalidOperationException { public InvalidOperationException() { } } }";
        Compilation legacy = CSharpCompilation.Create("Bare", [Parse($"{Framework} namespace N {{ public class C {{ {OpenSwitch} }} }}", LanguageVersion.CSharp14)]);
        Compilation neither = CSharpCompilation.Create("Bare", [Parse($"namespace N {{ public class C {{ {OpenSwitch} }} }}", LanguageVersion.CSharp14)]);

        Assert.Contains("context=System.InvalidOperationException::.ctor()\n", Text(legacy), StringComparison.Ordinal);
        Assert.False(Fingerprint(legacy, legacy: true).RuntimeSensitive);
        Assert.NotEqual(Fingerprint(legacy, legacy: true), Fingerprint(Compile(OpenSwitch), legacy: false));
        Assert.DoesNotContain("context=", Text(neither), StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterion 4: an expression whose arms cover every value (a discard or <c>var</c> arm with no <c>when</c> clause,
    /// or patterns that leave no value out) has no throw: its text names no constructor and the body is not
    /// runtime-sensitive. A <c>when</c> clause on the last arm, or a value left out (<c>null</c>), keeps the throw.
    /// </summary>
    [Theory]
    [InlineData("int M(int a) => a switch { 1 => 10, _ => 0 };", false)]
    [InlineData("int M(int a) => a switch { 1 => 10, var b => b };", false)]
    [InlineData("int M(bool b) => b switch { true => 1, false => 0 };", false)]
    [InlineData("int M(bool? b) => b switch { true => 1, false => 0, null => 2 };", false)]
    [InlineData("int M(bool? b) => b switch { true => 1, false => 0 };", true)]
    [InlineData("int M(string s) => s switch { \"a\" => 1, string t => t.Length };", true)]
    [InlineData("int M(System.DayOfWeek d) => d switch { System.DayOfWeek.Sunday => 1, System.DayOfWeek.Monday => 2, System.DayOfWeek.Tuesday => 3, System.DayOfWeek.Wednesday => 4, System.DayOfWeek.Thursday => 5, System.DayOfWeek.Friday => 6, System.DayOfWeek.Saturday => 7 };", true)]
    [InlineData("int M(int a) => a switch { 1 => 10, _ when a > 5 => 0 };", true)]
    [InlineData("int M(int a) => a switch { 1 => 10, var b when b > 5 => 0 };", true)]
    public void ASwitchExpressionWhoseArmAlwaysMatchesHasNoThrow(string member, bool throws)
    {
        Compilation compilation = Compile(member);

        Assert.Equal(throws, Text(compilation).Contains(NoMatchConstructor, StringComparison.Ordinal));
        Assert.Equal(throws, Fingerprint(compilation, legacy: false).RuntimeSensitive);
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
    /// whose implementing part does not bind, an abstract accessor, and a partial member that is not an
    /// ordinary method: a partial property's accessor, and a partial constructor, which runs its type's initializers too.
    /// And ADR 0054 decision 5 (ticket P2-145): an <c>extern</c> method that names no implementation.
    /// </summary>
    [Theory]
    [InlineData("partial void M();", "M")]
    [InlineData("public extern int M();", "M")]
    [InlineData("private static partial int M(); private static extern partial int M();", "M")]
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

    /// <summary>
    /// Ticket P2-145 criterion 1: an <c>extern</c> local function has no bound code, so its attributes are its code, in any
    /// body and on any runtime pair. Two bodies that differ only there are not congruent; two that name the same import are,
    /// also when one names the library through a constant.
    /// </summary>
    [Theory]
    [InlineData("\"b.dll\"", "", "int")]
    [InlineData("\"a.dll\", EntryPoint = \"G\"", "", "int")]
    [InlineData("\"a.dll\"", "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]", "int")]
    [InlineData("\"a.dll\"", "", "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] int")]
    public void BodiesThatDifferOnlyInALocalExternFunctionsAttributesAreNotCongruent(string import, string returns, string parameter)
    {
        Compilation baseline = Compile(Importer("\"a.dll\""));
        Compilation edited = Compile(Importer(import, returns, parameter));
        Compilation named = Compile("private const string Library = \"a.dll\"; " + Importer("Library"));

        foreach (SideRuntime runtime in (SideRuntime[])[OneRuntime, Runtimes.Migration])
        {
            BodyFingerprint? On(Compilation compilation) => BodyFingerprinter.Compute(Method(compilation), compilation, EquivConfig.Default, legacy: false, runtime);

            Assert.NotNull(On(baseline));
            Assert.NotEqual(On(baseline), On(edited));
            Assert.Equal(On(baseline), On(Compile(Importer("\"a.dll\""))));
            Assert.Equal(On(baseline), On(named));
        }
    }

    /// <summary>
    /// Ticket P2-145 criterion 2: a body that declares no local function with an attribute has the text it had before the
    /// attribute lines existed. The snapshot was taken on <c>main</c> before they were written.
    /// </summary>
    [Fact]
    public Task ABodyWithoutAttributedLocalFunctionsKeepsItsText()
    {
        Compilation plain = Compile("public int M(int a) { return F(a); static int F(int x) => x; }");

        Assert.Equal(Text(plain), BodyFingerprinter.Text(Method(plain), plain, EquivConfig.Default, [], OneRuntime).Text);
        Assert.DoesNotContain("Attribute ", Text(Compile("public int M(int a) { System.Func<int, int> f = x => x; return f(a); }")), StringComparison.Ordinal);
        return Verify(Text(plain));
    }

    /// <summary>
    /// Ticket P2-145 criterion 2: a local function's attribute lines follow its own line and are named by its number: those
    /// on the function, then on its return value, then on each parameter.
    /// </summary>
    [Fact]
    public void ALocalFunctionsAttributesFollowItsLine()
    {
        string attributed = Text(Compile(Importer("\"a.dll\"", "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]", "[System.Runtime.InteropServices.In] int")));
        Assert.Contains(
            "symbols=F0(async=False None System.Int32) -> System.Int32\n"
            + "Extern F0: module=\"a.dll\" entry=\"F\" charset=None convention=Winapi exact=False lastError=False bestFit= throwOnUnmappable=\n"
            + "Attribute F0: System.Runtime.InteropServices.DllImportAttribute.DllImportAttribute(string) = System.Runtime.InteropServices.DllImportAttribute(\"a.dll\")\n"
            + "Attribute F0 return: System.Runtime.InteropServices.MarshalAsAttribute.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType) = System.Runtime.InteropServices.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType.I4)\n"
            + "Attribute F0 #0: System.Runtime.InteropServices.InAttribute.InAttribute() = System.Runtime.InteropServices.InAttribute\n",
            attributed,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-145: what an <c>extern</c> local function imports is written as the compiler resolves it. With no
    /// <c>EntryPoint</c> the entry point is the function's own name, so renaming the function changes what the body calls;
    /// with one it does not. A function with no <c>[DllImport]</c> is written by its declared type and name.
    /// </summary>
    [Fact]
    public void AnExternLocalFunctionIsWrittenByWhatItImports()
    {
        static string Renamed(string import, string name) =>
            $"public int M(int a) {{ return {name}(a); [System.Runtime.InteropServices.DllImport({import})] static extern int {name}(int x); }}";
        const string Settings = "\"a.dll\", EntryPoint = \"E\", CharSet = System.Runtime.InteropServices.CharSet.Unicode, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl, "
            + "ExactSpelling = true, SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true";

        Assert.NotEqual(Fingerprint(Renamed("\"a.dll\"", "F")), Fingerprint(Renamed("\"a.dll\"", "G")));
        Assert.Equal(Fingerprint(Renamed("\"a.dll\", EntryPoint = \"E\"", "F")), Fingerprint(Renamed("\"a.dll\", EntryPoint = \"E\"", "G")));
        Assert.Contains(
            "\nExtern F0: module=\"a.dll\" entry=\"E\" charset=Unicode convention=Cdecl exact=True lastError=True bestFit=False throwOnUnmappable=True\n",
            Text(Compile(Renamed(Settings, "F"))),
            StringComparison.Ordinal);
        Assert.Contains("\nExtern F0: \"N.C::F\"\n", Text(Compile("public int M() { return F(); static extern int F(); }")), StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR 0054 decision 2 (ticket P2-145 criterion 4): on one runtime an <c>extern</c> method is fingerprinted by its
    /// signature, by what it imports as the compiler resolves it, and by its attributes and those of its return value and
    /// parameters. So an unedited one is congruent, and one whose import or marshalling changed is not.
    /// </summary>
    [Theory]
    [InlineData("[DllImport(\"b.dll\")] public static extern int M(int a);")]
    [InlineData("[DllImport(\"a.dll\", EntryPoint = \"G\")] public static extern int M(int a);")]
    [InlineData("[DllImport(\"a.dll\", CharSet = CharSet.Unicode)] public static extern int M(int a);")]
    [InlineData("[DllImport(\"a.dll\")] [return: MarshalAs(UnmanagedType.I4)] public static extern int M(int a);")]
    [InlineData("[DllImport(\"a.dll\")] public static extern int M([MarshalAs(UnmanagedType.I4)] int a);")]
    [InlineData("[DllImport(\"a.dll\")] [SuppressGCTransition] public static extern int M(int a);")]
    [InlineData("[DllImport(\"a.dll\")] public static extern int M(ref int a);")]
    [InlineData("[DllImport(\"a.dll\")] public static extern long M(int a);")]
    public void AnExternMethodIsFingerprintedByItsSignatureAndAttributes(string edited)
    {
        const string Import = "[DllImport(\"a.dll\")] public static extern int M(int a);";
        Compilation baseline = Interop(Import);

        Assert.Equal(
            "Ordinary static=True async=False returns=System.Int32 (None System.Int32)\n"
            + "Extern method: module=\"a.dll\" entry=\"M\" charset=None convention=Winapi exact=False lastError=False bestFit= throwOnUnmappable=\n"
            + "Attribute method: System.Runtime.InteropServices.DllImportAttribute.DllImportAttribute(string) = System.Runtime.InteropServices.DllImportAttribute(\"a.dll\")\n",
            BodyFingerprinter.Text(Method(baseline), baseline, EquivConfig.Default, [], OneRuntime).Text);
        Assert.False(OnOneRuntime(baseline)!.RuntimeSensitive);
        Assert.Equal(OnOneRuntime(baseline), OnOneRuntime(Interop("[DllImport(\"a.dll\")] public static extern int M(int renamed);")));
        Assert.Equal(OnOneRuntime(baseline), OnOneRuntime(Interop("private const string Library = \"a.dll\"; [DllImport(Library)] public static extern int M(int a);")));
        Assert.NotEqual(OnOneRuntime(baseline), OnOneRuntime(Interop(edited)));
    }

    /// <summary>
    /// ADR 0054 decision 2: with no <c>EntryPoint</c> the entry point is the method's own name, which a rename map can
    /// match to another, so the name is in the text; with one, the method's name is not what is called.
    /// </summary>
    [Fact]
    public void AnExternMethodsEntryPointIsItsNameUnlessTheImportNamesOne()
    {
        static BodyFingerprint? Named(string import, string name)
        {
            Compilation compilation = Interop($"[DllImport({import})] public static extern int {name}(int a);");
            return BodyFingerprinter.Compute(Method(compilation, name), compilation, EquivConfig.Default, legacy: false, OneRuntime);
        }

        Assert.NotEqual(Named("\"a.dll\"", "F"), Named("\"a.dll\"", "G"));
        Assert.Equal(Named("\"a.dll\", EntryPoint = \"E\"", "F"), Named("\"a.dll\", EntryPoint = \"E\"", "G"));
    }

    /// <summary>
    /// ADR 0054 decisions 1 and 2: every kind of <c>extern</c> member that names its implementation has a fingerprint on one
    /// runtime. An <c>InternalCall</c> has its declared type and name in place of an import, and a partial method whose
    /// implementing part is <c>extern</c> has the attributes of both its parts.
    /// </summary>
    [Theory]
    [InlineData("[MethodImpl(MethodImplOptions.InternalCall)] public extern int M();", "M", "Extern method: \"N.C::M\"\nAttribute method: System.Runtime.CompilerServices.MethodImplAttribute")]
    [InlineData("[MethodImpl(MethodImplOptions.InternalCall)] public extern C(int x);", ".ctor", "Constructor static=False async=False returns=void (None System.Int32)\nExtern method: \"N.C::.ctor\"\n")]
    [InlineData("public static extern int P { [DllImport(\"a.dll\")] get; }", "get_P", "PropertyGet static=True async=False returns=System.Int32 ()\nExtern method: module=\"a.dll\" entry=\"get_P\"")]
    [InlineData("[DllImport(\"a.dll\")] public static extern C operator +(C a, C b);", "op_Addition", "Extern method: module=\"a.dll\" entry=\"op_Addition\"")]
    [InlineData("[System.Obsolete] private static partial int M(); [DllImport(\"a.dll\")] private static extern partial int M();", "M", "entry=\"M\" charset=None convention=Winapi exact=False lastError=False bestFit= throwOnUnmappable=\nAttribute method: System.ObsoleteAttribute.ObsoleteAttribute() = System.ObsoleteAttribute\nAttribute method: System.Runtime.InteropServices.DllImportAttribute")]
    public void EveryKindOfExternMemberThatNamesItsImplementationHasAFingerprint(string member, string name, string expected)
    {
        Compilation compilation = Interop(member);

        Assert.Contains(expected, BodyFingerprinter.Text(Method(compilation, name), compilation, EquivConfig.Default, [], OneRuntime).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-146: <c>[assembly: DisableRuntimeMarshalling]</c> turns marshalling off for every <c>[DllImport]</c> in the
    /// assembly, so it is in the text of each one, a local function's included, on any runtime pair.
    /// </summary>
    [Fact]
    public void AnAssemblysDisabledRuntimeMarshallingIsInAnExternFunctionsText()
    {
        Compilation disabled = Interop(Imported, Disabled);

        Assert.NotEqual(OnOneRuntime(Interop(Imported)), OnOneRuntime(disabled));
        Assert.Equal(OnOneRuntime(Interop(Imported, Disabled)), OnOneRuntime(disabled));
        Assert.EndsWith(
            "DllImportAttribute(\"a.dll\")\n"
            + "Attribute method assembly: System.Runtime.CompilerServices.DisableRuntimeMarshallingAttribute.DisableRuntimeMarshallingAttribute() = System.Runtime.CompilerServices.DisableRuntimeMarshallingAttribute\n",
            OneRuntimeText(disabled),
            StringComparison.Ordinal);
        AssertALocalImportTakes(Disabled);
    }

    /// <summary>
    /// Ticket P2-146: the assembly's <c>[DefaultDllImportSearchPaths]</c> says where the library of an import that has none
    /// of its own is looked for. An import that has its own keeps its text.
    /// </summary>
    [Fact]
    public void AnAssemblysImportSearchPathsAreInTheTextOfAnExternFunctionThatNamesNone()
    {
        static string Paths(string path) => $"[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.{path})]";
        const string Own = "[DllImport(\"a.dll\")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] public static extern int M(int a);";

        Assert.NotEqual(OnOneRuntime(Interop(Imported)), OnOneRuntime(Interop(Imported, Paths("System32"))));
        Assert.NotEqual(OnOneRuntime(Interop(Imported, Paths("SafeDirectories"))), OnOneRuntime(Interop(Imported, Paths("System32"))));
        Assert.Contains("Attribute method assembly: System.Runtime.InteropServices.DefaultDllImportSearchPathsAttribute.", OneRuntimeText(Interop(Imported, Paths("System32"))), StringComparison.Ordinal);
        Assert.Equal(OneRuntimeText(Interop(Own)), OneRuntimeText(Interop(Own, Paths("SafeDirectories"))));
        Assert.DoesNotContain(" assembly", OneRuntimeText(Interop(Own, Paths("SafeDirectories"))), StringComparison.Ordinal);
        AssertALocalImportTakes(Paths("System32"));
    }

    /// <summary>
    /// Ticket P2-146: an import that leaves <c>BestFitMapping</c> or <c>ThrowOnUnmappableChar</c> open takes it from the
    /// <c>[BestFitMapping]</c> of its type or of the assembly. An import that names both keeps its text.
    /// </summary>
    [Theory]
    [InlineData("[assembly: System.Runtime.InteropServices.BestFitMapping(false)]", "", "[assembly: System.Runtime.InteropServices.BestFitMapping(true)]", "", "Attribute method assembly: System.Runtime.InteropServices.BestFitMappingAttribute.")]
    [InlineData("", "[BestFitMapping(false)]", "", "[BestFitMapping(true)]", "Attribute method type: System.Runtime.InteropServices.BestFitMappingAttribute.")]
    [InlineData("", "[BestFitMapping(true, ThrowOnUnmappableChar = true)]", "", "[BestFitMapping(true, ThrowOnUnmappableChar = false)]", "Attribute method type: System.Runtime.InteropServices.BestFitMappingAttribute.")]
    public void ABestFitMappingOnTheTypeOrTheAssemblyIsInTheTextOfAnImportThatLeavesItOpen(string assembly, string type, string otherAssembly, string otherType, string line)
    {
        const string Own = "[DllImport(\"a.dll\", BestFitMapping = true, ThrowOnUnmappableChar = false)] public static extern int M(int a);";
        string[] open =
        [
            Imported,
            "[DllImport(\"a.dll\", BestFitMapping = true)] public static extern int M(int a);",
            "[DllImport(\"a.dll\", ThrowOnUnmappableChar = false)] public static extern int M(int a);",
        ];

        foreach (string import in open)
        {
            Assert.NotEqual(OnOneRuntime(Interop(import)), OnOneRuntime(Interop(import, assembly, type)));
            Assert.NotEqual(OnOneRuntime(Interop(import, otherAssembly, otherType)), OnOneRuntime(Interop(import, assembly, type)));
            Assert.Contains(line, OneRuntimeText(Interop(import, assembly, type)), StringComparison.Ordinal);
        }

        Assert.Equal(OneRuntimeText(Interop(Own)), OneRuntimeText(Interop(Own, assembly, type)));
    }

    /// <summary>
    /// Ticket P2-146: how an argument is marshalled is also in the declaration of its type: a type's layout, a field's
    /// <c>[MarshalAs]</c> and <c>[FieldOffset]</c>, the order and the types of the fields, a delegate's
    /// <c>[UnmanagedFunctionPointer]</c> and its own signature. The types are those the signature reaches: through an
    /// array, a pointer, a reference, a function pointer, a type argument, a field and a base type.
    /// </summary>
    [Theory]
    [InlineData("public struct S { public int A; public byte B; }", "[StructLayout(LayoutKind.Sequential, Pack = 1)] public struct S { public int A; public byte B; }", "int M(S s)")]
    [InlineData("[StructLayout(LayoutKind.Sequential)] public struct S { public int A; }", "[StructLayout(LayoutKind.Sequential, Size = 16)] public struct S { public int A; }", "int M(S s)")]
    [InlineData("[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)] public struct S { public string A; }", "[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct S { public string A; }", "int M(S s)")]
    [InlineData("public struct S { public bool A; }", "public struct S { [MarshalAs(UnmanagedType.I1)] public bool A; }", "int M(S s)")]
    [InlineData("[StructLayout(LayoutKind.Explicit)] public struct S { [FieldOffset(0)] public int A; [FieldOffset(4)] public int B; }", "[StructLayout(LayoutKind.Explicit)] public struct S { [FieldOffset(0)] public int A; [FieldOffset(0)] public int B; }", "int M(S s)")]
    [InlineData("public struct S { public int A; public byte B; }", "public struct S { public byte B; public int A; }", "int M(S s)")]
    [InlineData("public struct S { public int A; }", "public struct S { public int A; public int B; }", "int M(S s)")]
    [InlineData("public unsafe struct S { public fixed byte A[4]; }", "public unsafe struct S { public fixed byte A[8]; }", "int M(S s)")]
    [InlineData("public struct S { public int A { get; set; } }", "public struct S { [field: MarshalAs(UnmanagedType.U4)] public int A { get; set; } }", "int M(S s)")]
    [InlineData("public struct S { public int A; }", "public class S { public int A; }", "int M(S s)")]
    [InlineData("public enum S { A }", "public enum S : byte { A }", "int M(S s)")]
    [InlineData("public delegate int S(int x);", "[UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int S(int x);", "int M(S s)")]
    [InlineData("public delegate int S(bool x);", "public delegate int S([MarshalAs(UnmanagedType.I1)] bool x);", "int M(S s)")]
    [InlineData("public delegate bool S(int x);", "[return: MarshalAs(UnmanagedType.I1)] public delegate bool S(int x);", "int M(S s)")]
    [InlineData("public delegate int S(int x);", "public delegate int S(ref int x);", "int M(S s)")]
    [InlineData("public delegate int S(int x);", "public delegate long S(int x);", "int M(S s)")]
    [InlineData("public struct T { public bool A; } public delegate int S(T x);", "public struct T { [MarshalAs(UnmanagedType.I1)] public bool A; } public delegate int S(T x);", "int M(S s)")]
    [InlineData("public struct T { public bool A; } public delegate T S();", "public struct T { [MarshalAs(UnmanagedType.I1)] public bool A; } public delegate T S();", "int M(S s)")]
    [InlineData("public struct S { public bool A; }", "public struct S { [MarshalAs(UnmanagedType.I1)] public bool A; }", "S M()")]
    [InlineData("public struct S { public bool A; }", "public struct S { [MarshalAs(UnmanagedType.I1)] public bool A; }", "int M(int a, ref S s)")]
    [InlineData("public struct S { public bool A; }", "public struct S { [MarshalAs(UnmanagedType.I1)] public bool A; }", "int M(S[] s)")]
    [InlineData("public struct S { public bool A; }", "public struct S { [MarshalAs(UnmanagedType.I1)] public bool A; }", "unsafe int M(S* s)")]
    [InlineData("public struct S { public bool A; }", "public struct S { [MarshalAs(UnmanagedType.I1)] public bool A; }", "unsafe int M(delegate* unmanaged<S, int> s)")]
    [InlineData("public struct S { public bool A; }", "public struct S { [MarshalAs(UnmanagedType.I1)] public bool A; }", "unsafe int M(delegate* unmanaged<int, S> s)")]
    [InlineData("public struct S { public bool A; }", "public struct S { [MarshalAs(UnmanagedType.I1)] public bool A; }", "int M(System.Span<S> s)")]
    [InlineData("public struct G<X> { public X A; } public struct S { public bool A; }", "public struct G<X> { public X A; } public struct S { [MarshalAs(UnmanagedType.I1)] public bool A; }", "int M(G<S> s)")]
    [InlineData("public struct T { public bool A; } public struct S { public T A; }", "public struct T { [MarshalAs(UnmanagedType.I1)] public bool A; } public struct S { public T A; }", "int M(S s)")]
    [InlineData("public class T { public bool A; } public class S : T { }", "public class T { [MarshalAs(UnmanagedType.I1)] public bool A; } public class S : T { }", "int M(S s)")]
    [InlineData("public class T { } public class U { } public class S : T { }", "public class T { } public class U { } public class S : U { }", "int M(S s)")]
    public void TheLayoutOfATypeInAnExternFunctionsSignatureIsInItsText(string legacy, string modern, string signature)
    {
        string method = $" [DllImport(\"a.dll\")] public static extern {signature};";
        string local = $" public void L() {{ [DllImport(\"a.dll\")] static extern {signature}; }}";

        Assert.NotEqual(OnOneRuntime(Interop(legacy + method)), OnOneRuntime(Interop(modern + method)));
        Assert.Equal(OnOneRuntime(Interop(modern + method)), OnOneRuntime(Interop(modern + method)));
        foreach (SideRuntime runtime in (SideRuntime[])[OneRuntime, Runtimes.Migration])
        {
            BodyFingerprint? On(string members)
            {
                Compilation compilation = Interop(members);
                return BodyFingerprinter.Compute(Method(compilation, "L"), compilation, EquivConfig.Default, legacy: false, runtime);
            }

            Assert.NotNull(On(legacy + local));
            Assert.NotEqual(On(legacy + local), On(modern + local));
            Assert.Equal(On(modern + local), On(modern + local));
        }
    }

    /// <summary>
    /// Ticket P2-146: what a type in the signature is written as. Each type declared in the solution once, however often
    /// the signature reaches it and although it holds a pointer to itself; its attributes; its instance fields in
    /// declaration order, each with its type, a fixed buffer's length and its attributes; a delegate's signature. A static
    /// field is no part of the layout, and a type from a reference has no lines: its name is in the signature.
    /// </summary>
    [Fact]
    public Task TheTypesInAnExternFunctionsSignatureAreWrittenOnceEachWithTheirFields()
    {
        const string Types = "[StructLayout(LayoutKind.Sequential, Pack = 2)] public unsafe struct S { public static int Z; [MarshalAs(UnmanagedType.I1)] public bool A; public fixed byte B[4]; public S* Next; public E Kind; public System.Guid Id; } "
            + "public enum E : byte { A } public class P { public int A; } public class Q : P { public G<long> B; } public struct G<X> { public X A; } "
            + "[UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public delegate bool D([In] ref S s, int x); ";
        const string Function = "[DllImport(\"a.dll\")] public static extern unsafe S M(S s, D d, Q q, S[] again, int plain);";
        Compilation compilation = Interop(Types + Function);

        Assert.Equal(OneRuntimeText(compilation), OneRuntimeText(Interop(Types.Replace("public static int Z; ", string.Empty, StringComparison.Ordinal) + Function)));
        return Verify(OneRuntimeText(compilation));
    }

    /// <summary>
    /// Ticket P2-146 criterion 2: the settings are in the text of an <c>extern</c> function that takes them and in no
    /// other. A body that declares none, although it uses a type with a layout, and an <c>InternalCall</c>, which the
    /// marshaller never sees, have the text they have without the settings.
    /// </summary>
    [Theory]
    [InlineData("public int M(S s) { return F(s); [System.Obsolete] static int F(S x) => x.A; }")]
    [InlineData("[MethodImpl(MethodImplOptions.InternalCall)] public extern int M(S s);")]
    public void ABodyWithoutAnExternFunctionKeepsItsText(string member)
    {
        const string Laid = "[StructLayout(LayoutKind.Sequential, Pack = 1)] public struct S { public int A; } ";
        const string Settings = Disabled
            + "[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]"
            + "[assembly: System.Runtime.InteropServices.BestFitMapping(false)]";

        string text = OneRuntimeText(Interop(Laid + member, Settings, "[BestFitMapping(false)]"));

        Assert.Equal(OneRuntimeText(Interop(Laid + member)), text);
        Assert.DoesNotContain("Marshalled", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BestFitMapping", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-149: two fields of a <c>[StructLayout(LayoutKind.Explicit)]</c> type at one <c>[FieldOffset]</c> are one
    /// storage location and at two offsets they are two, so the type's declaration is in the text of a body that reads or
    /// writes such a field, of a type nested in another as well.
    /// </summary>
    [Theory]
    [InlineData("public int M(S s) { s.A = 1; return s.B; }")]
    [InlineData("public int M(S s) => s.B;")]
    [InlineData("public void M(ref S s) { s.A = 1; }")]
    [InlineData("public int M(O o) => o.Inner.B;")]
    public void AFieldOffsetIsInTheTextOfABodyThatReadsOrWritesTheField(string member)
    {
        const string Outer = " public struct O { public S Inner; }";

        AssertTheBodyDependsOn(Overlaid("4") + Outer, Overlaid("0") + Outer, member);
        Assert.Contains("Attribute layout N.C.S #1: System.Runtime.InteropServices.FieldOffsetAttribute.", OneRuntimeText(Interop(Overlaid("4") + Outer + member)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-149: a type's size and the offsets of its fields are fixed by its <c>[StructLayout]</c>, by the order and
    /// the types of its fields and by those of the types its fields have. A body that takes the type's size, asks the
    /// interop services about it, or reads it through a pointer or out of a span of bytes depends on them.
    /// </summary>
    [Theory]
    [InlineData("public struct S { public byte A; public int B; }", "[StructLayout(LayoutKind.Sequential, Pack = 1)] public struct S { public byte A; public int B; }")]
    [InlineData("[StructLayout(LayoutKind.Sequential)] public struct S { public byte A; public int B; }", "[StructLayout(LayoutKind.Sequential, Size = 16)] public struct S { public byte A; public int B; }")]
    [InlineData("public struct S { public byte A; public int B; public byte C; }", "public struct S { public byte A; public byte C; public int B; }")]
    [InlineData("public struct S { public byte A; public int B; }", "public struct S { public byte A; public int B; public int C; }")]
    [InlineData("public struct T { public byte A; } public struct S { public T A; public int B; }", "public struct T { public long A; } public struct S { public T A; public int B; }")]
    public void ATypesLayoutIsInTheTextOfABodyThatTakesItsSizeOrReadsItAsBytes(string legacy, string modern)
    {
        string[] members =
        [
            "public unsafe int M() => sizeof(S);",
            "public int M() => Marshal.SizeOf<S>();",
            "public int M() => Marshal.SizeOf(typeof(S));",
            "public void M(S s, System.IntPtr p) => Marshal.StructureToPtr((object)s, p, false);",
            "public int M() => Unsafe.SizeOf<S>();",
            "public int M() => Marshal.SizeOf<int>() + Unsafe.SizeOf<S>();",
            "public int M(S[] all) => System.Runtime.InteropServices.Marshalling.ArrayMarshaller<S, S>.GetManagedValuesSource(all).Length;",
            "public unsafe int M(byte* p) => ((S*)p)->B;",
            "public unsafe int M(S* p) => p[1].B;",
            "public unsafe int M(delegate*<S, int> f, S s) => f(s);",
            "public int M(System.ReadOnlySpan<byte> b) => MemoryMarshal.Read<S>(b).B;",
            "public int M(System.Span<byte> b) => MemoryMarshal.Cast<byte, S>(b)[0].B;",
            "public unsafe int M(void* p) => new System.Span<S>(p, 1)[0].B;",
            "public unsafe int M(byte* p) => R<S>(p).B; private static unsafe T R<T>(byte* p) where T : unmanaged => *(T*)p;",
        ];

        foreach (string member in members)
        {
            AssertTheBodyDependsOn(legacy, modern, member);
        }
    }

    /// <summary>
    /// Ticket P2-149: the length of an inline array is the argument of its <c>[InlineArray]</c>, so it is in the text of
    /// a body that indexes one, converts one to a span or enumerates one.
    /// </summary>
    [Theory]
    [InlineData("public int M(B b, int i) => b[i];")]
    [InlineData("public int M(B b) { System.Span<int> s = b; return s.Length; }")]
    [InlineData("public int M(B b) { int n = 0; foreach (int x in b) { n += x; } return n; }")]
    public void AnInlineArraysLengthIsInTheTextOfABodyThatUsesIt(string member)
    {
        static string Buffer(string length) => $"[InlineArray({length})] public struct B {{ private int e; }} ";

        AssertTheBodyDependsOn(Buffer("4"), Buffer("8"), member);
        Assert.Contains("Attribute layout N.C.B: System.Runtime.CompilerServices.InlineArrayAttribute.", OneRuntimeText(Interop(Buffer("4") + member)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-149: a delegate type's <c>[UnmanagedFunctionPointer]</c> is the calling convention of the function
    /// pointer the interop services make of a delegate, and the assembly's <c>[DisableRuntimeMarshalling]</c> says whether
    /// a call through a function pointer marshals its arguments.
    /// </summary>
    [Theory]
    [InlineData("public System.IntPtr M(D d) => Marshal.GetFunctionPointerForDelegate(d);", true)]
    [InlineData("public System.IntPtr M(D d) => Marshal.GetFunctionPointerForDelegate((System.Delegate)d);", true)]
    [InlineData("public D M(System.IntPtr p) => Marshal.GetDelegateForFunctionPointer<D>(p);", true)]
    [InlineData("public unsafe int M(delegate* unmanaged<int, int> f) => f(1);", false)]
    public void WhatAFunctionPointerTakesFromADelegateTypeAndFromTheAssemblyIsInTheBodysText(string member, bool namesTheDelegateType)
    {
        const string Plain = "public delegate int D(int x); ";
        const string Cdecl = "[UnmanagedFunctionPointer(CallingConvention.Cdecl)] " + Plain;

        if (namesTheDelegateType)
        {
            AssertTheBodyDependsOn(Plain, Cdecl, member);
        }

        AssertTheBodyDependsOn(Plain, Plain, member, modernAssembly: Disabled);
        Assert.Contains("Attribute layout assembly: System.Runtime.CompilerServices.DisableRuntimeMarshallingAttribute.", OneRuntimeText(Interop(Plain + member, Disabled)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-149 criterion 3: a body that does none of those things keeps the text it has, although the types it
    /// uses have a layout and its assembly turns runtime marshalling off: it copies a value, reads a field that shares
    /// no storage, holds the type in an array or a list, or names it in a <c>typeof</c>.
    /// </summary>
    [Theory]
    [InlineData("public int M(S s) { S t = s; return t.B + s.A; }")]
    [InlineData("public S M(S[] all) => all[0];")]
    [InlineData("public int M(System.Collections.Generic.List<S> all) => all.Count + all[0].B;")]
    [InlineData("public string M(S s) => typeof(S).Name + s.ToString();")]
    [InlineData("public E M(E e) { E f = e; return f; }")]
    [InlineData("public int M(int a) => sizeof(int) + a + new S().B;")]
    [InlineData("public int M(D d) => d(1);")]
    public void ABodyThatUsesNoLaidOutTypeKeepsItsText(string member)
    {
        const string Plain = "public struct S { public byte A; public int B; } public struct E { public int A; } public delegate int D(int x); ";
        const string Laid = "[StructLayout(LayoutKind.Sequential, Pack = 1)] public struct S { public byte A; public int B; } "
            + "[StructLayout(LayoutKind.Explicit)] public struct E { [FieldOffset(0)] public int A; } "
            + "[UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int D(int x); ";
        Compilation laid = Interop(Laid + member, Disabled);

        Assert.Empty(laid.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(OneRuntimeText(Interop(Plain + member)), OneRuntimeText(laid));
        Assert.Equal(Text(Interop(Plain + member)), Text(laid));
        Assert.DoesNotContain("Marshalled", OneRuntimeText(laid), StringComparison.Ordinal);
        Assert.DoesNotContain(" assembly", OneRuntimeText(laid), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-151: a generic member of the solution can read the layout of its type parameter, and its text is the
    /// same for every type argument. So the declaration of a type the solution declares is in the text of the body that
    /// hands it over: to a generic method, a local function, or any member of a generic type, by naming it, by
    /// inference, inside another type argument, through a forwarder or through a base type.
    /// </summary>
    [Theory]
    [InlineData("public int M() => Size<S>();")]
    [InlineData("public int M(S s) => SizeOf(s);")]
    [InlineData("public System.Func<int> M() => Size<S>;")]
    [InlineData("public int M() => Size<P<S>>();")]
    [InlineData("public int M() => Size<(S, int)>();")]
    [InlineData("public int M() => SizeOfS();")]
    [InlineData("public int M() { return Local<S>(); static unsafe int Local<T>() where T : unmanaged => sizeof(T); }")]
    [InlineData("public int M() => Box<S>.Size();")]
    [InlineData("public int M() => Box<S>.Property;")]
    [InlineData("public int M() => Box<S>.Field;")]
    [InlineData("public int M() => new Box<S>().Instance;")]
    [InlineData("public int M(Box<S> b) => b.Instance;")]
    [InlineData("public int M(Derived d) => d.Instance;")]
    [InlineData("public int M() => Box<S>.Nested.Size();")]
    public void ATypeArgumentsLayoutIsInTheTextOfACallToAMemberThatReadsIt(string member)
    {
        const string Generic = " static unsafe int Size<T>() where T : unmanaged => sizeof(T);"
            + " static unsafe int SizeOf<T>(T value) where T : unmanaged => sizeof(T);"
            + " static int SizeOfS() => Size<S>();"
            + " public struct P<T> { public T First; public byte Second; }"
            + " public class Derived : Box<S> { }"
            + " public unsafe class Box<T> where T : unmanaged { public static readonly int Field = sizeof(T); public int Instance = sizeof(T);"
            + " public static int Property => sizeof(T); public static int Size() => sizeof(T); public static class Nested { public static int Size() => sizeof(T); } }";

        AssertTheBodyDependsOn("public struct S { public byte A; public int B; }", "[StructLayout(LayoutKind.Sequential, Pack = 1)] public struct S { public byte A; public int B; }", member + Generic);
        AssertTheBodyDependsOn("public struct S { public byte A; public int B; }", "public struct S { public byte A; public int B; public int C; }", member + Generic);
    }

    /// <summary>
    /// Ticket P2-151 criterion 3: a call that hands a generic member no type of the solution keeps the text it has,
    /// whatever layout the types beside it have: its type argument is a type from a reference or a type parameter, the
    /// member is not generic, or it is a generic member of a reference.
    /// </summary>
    [Theory]
    [InlineData("public int M() => Size<int>() + Size<System.Guid>() + Box<long>.Size;")]
    [InlineData("public int M<U>() where U : unmanaged => Size<U>() + Box<U>.Size;")]
    [InlineData("public int M(S s) => Plain(s) + s.B;")]
    [InlineData("public int M(S s) => new System.Collections.Generic.List<S> { s }.Count + System.Array.Empty<S>().Length;")]
    public void ACallThatHandsOverNoTypeOfTheSolutionKeepsItsText(string member)
    {
        const string Generic = " static unsafe int Size<T>() where T : unmanaged => sizeof(T); static int Plain(S s) => s.A;"
            + " public static unsafe class Box<T> where T : unmanaged { public static int Size => sizeof(T); }";
        Compilation plain = Interop("public struct S { public byte A; public int B; } " + member + Generic);
        Compilation laid = Interop("[StructLayout(LayoutKind.Sequential, Pack = 1)] public struct S { public byte A; public int B; } " + member + Generic);

        Assert.Empty(laid.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(OneRuntimeText(plain), OneRuntimeText(laid));
        Assert.Equal(Text(plain), Text(laid));
        Assert.DoesNotContain("Marshalled", OneRuntimeText(laid), StringComparison.Ordinal);
    }

    /// <summary>ADR 0054 decision 4: across runtimes it is the runtime that marshals the call, so an <c>extern</c> method has no fingerprint.</summary>
    [Theory]
    [InlineData("net48", "net10.0")]
    [InlineData("net8.0", "net9.0")]
    public void AnExternMethodHasNoFingerprintOnAPairThatCrossesARuntime(string legacy, string modern)
    {
        Compilation compilation = Interop("[DllImport(\"a.dll\")] public static extern int M(int a);");

        Assert.Null(BodyFingerprinter.Compute(Method(compilation), compilation, EquivConfig.Default, legacy: false, Runtimes.Between(legacy, modern)));
    }

    /// <summary>
    /// ADR 0054 decision 4: a body that declares an <c>extern</c> local function makes the same call, so on a pair that
    /// crosses a runtime it is runtime-sensitive and never congruent. On one runtime it is not, and neither is a body whose
    /// attributed local function has code.
    /// </summary>
    [Fact]
    public void ABodyThatDeclaresAnExternLocalFunctionIsRuntimeSensitiveAcrossARuntime()
    {
        Compilation importer = Compile(Importer("\"a.dll\""));
        Compilation coded = Compile("public int M(int a) { return F(a); [System.Obsolete] static int F(int x) => x; }");

        Assert.True(Fingerprint(importer, legacy: false).RuntimeSensitive);
        Assert.False(OnOneRuntime(importer)!.RuntimeSensitive);
        Assert.False(Fingerprint(coded, legacy: false).RuntimeSensitive);
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

    /// <summary>
    /// Ticket P2-137: a legacy member may have several entries, one per argument count. The fingerprint names a callee
    /// without its arguments, so it takes the first pass-through entry in file order, as it did when there was one.
    /// </summary>
    [Fact]
    public void ALegacyMemberWithTwoPassThroughEntriesIsNamedByTheFirst()
    {
        const string Legacy = "namespace Old { public class B { public int Ok() => 0; } } namespace N { public class C : Old.B { public int M() => Ok(); } }";
        const string Modern = "namespace Old { public class B { public int Fine() => 0; } } namespace N { public class C : Old.B { public int M() => Fine(); } }";
        ImmutableArray<ApiEquivalence> entries =
        [
            new("fine", IsType: false, "Old.B::Ok()", "Old.B::Fine()", [new ApiArgument(0)], "r", new Uri("https://learn.microsoft.com/")),
            new("other", IsType: false, "Old.B::Ok()", "Old.B::Other()", [new ApiArgument(0)], "r", new Uri("https://learn.microsoft.com/")),
        ];
        Compilation legacy = RoslynTestCompilations.Compile(Legacy);
        Compilation modern = RoslynTestCompilations.Compile(Modern);
        BodyFingerprint? modernPrint = BodyFingerprinter.Compute(Method(modern), modern, EquivConfig.Default, [], Runtimes.Migration);

        Assert.Equal(modernPrint, BodyFingerprinter.Compute(Method(legacy), legacy, EquivConfig.Default, entries, Runtimes.Migration));
        Assert.NotEqual(modernPrint, BodyFingerprinter.Compute(Method(legacy), legacy, EquivConfig.Default, [entries[1], entries[0]], Runtimes.Migration));
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

    /// <summary>A <c>switch</c> expression with no arm for most values, and the constructor the compiler calls for them on .NET.</summary>
    private const string OpenSwitch = "int M(int a) => a switch { 1 => 10, 2 => 20 };";

    private const string NoMatchConstructor = "System.Runtime.CompilerServices.SwitchExpressionException::.ctor()";

    /// <summary>A side of a pair whose two projects run on one runtime, so the pair crosses none (ADR 0040 decision 2).</summary>
    private static SideRuntime OneRuntime => Runtimes.Between("net8.0", "net8.0");

    private static BodyFingerprint? OnOneRuntime(Compilation compilation) =>
        BodyFingerprinter.Compute(Method(compilation), compilation, EquivConfig.Default, legacy: false, OneRuntime);

    /// <summary>A partial method and an implementing part that calls a local <c>extern</c> function, as the interop generator writes one.</summary>
    private static string Stub(string import, string returns, string parameter) =>
        $"private static partial bool M(int a); private static partial bool M(int a) {{ return __PInvoke(a) != 0; [System.Runtime.InteropServices.DllImport({import})] static extern {returns} __PInvoke({parameter} x); }}";

    /// <summary>Members of a partial class <c>N.C</c>, with the interop namespaces imported.</summary>
    private static CSharpCompilation Interop(string members, string assembly = "", string type = "") =>
        Compile(members, extra: "using System.Runtime.CompilerServices; using System.Runtime.InteropServices; " + type, partial: true, header: assembly);

    /// <summary>An <c>extern</c> method that names nothing but its library.</summary>
    private const string Imported = "[DllImport(\"a.dll\")] public static extern int M(int a);";

    private const string Disabled = "[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]";

    private static string OneRuntimeText(Compilation compilation) =>
        BodyFingerprinter.Text(Method(compilation), compilation, EquivConfig.Default, [], OneRuntime).Text!;

    /// <summary>A body whose local <c>extern</c> function takes the <paramref name="assembly"/>'s setting has another fingerprint with it, on any runtime pair.</summary>
    private static void AssertALocalImportTakes(string assembly)
    {
        Compilation plain = Compile(Importer("\"a.dll\""));
        Compilation set = Compile(Importer("\"a.dll\""), header: assembly);
        foreach (SideRuntime runtime in (SideRuntime[])[OneRuntime, Runtimes.Migration])
        {
            Assert.NotEqual(
                BodyFingerprinter.Compute(Method(plain), plain, EquivConfig.Default, legacy: false, runtime),
                BodyFingerprinter.Compute(Method(set), set, EquivConfig.Default, legacy: false, runtime));
        }
    }

    /// <summary>A type with two fields, the second at <paramref name="offset"/>: at 0 it shares the first one's storage.</summary>
    private static string Overlaid(string offset) =>
        $"[StructLayout(LayoutKind.Explicit)] public struct S {{ [FieldOffset(0)] public int A; [FieldOffset({offset})] public int B; }}";

    /// <summary>
    /// Ticket P2-149: <paramref name="member"/>'s body, which is the same code beside both sets of types and in both
    /// assemblies, has one fingerprint beside <paramref name="legacy"/> and another beside <paramref name="modern"/>, on
    /// one runtime and across one, and each is the same every time.
    /// </summary>
    private static void AssertTheBodyDependsOn(string legacy, string modern, string member, string modernAssembly = "")
    {
        foreach (SideRuntime runtime in (SideRuntime[])[OneRuntime, Runtimes.Migration])
        {
            BodyFingerprint? On(string types, string assembly)
            {
                Compilation compilation = Interop(types + " " + member, assembly);
                Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
                return BodyFingerprinter.Compute(Method(compilation), compilation, EquivConfig.Default, legacy: false, runtime);
            }

            Assert.NotNull(On(legacy, string.Empty));
            Assert.NotEqual(On(legacy, string.Empty), On(modern, modernAssembly));
            Assert.Equal(On(modern, modernAssembly), On(modern, modernAssembly));
        }
    }

    /// <summary>A method that calls a local <c>extern</c> function it declares: ticket P2-145's first repro.</summary>
    private static string Importer(string import, string returns = "", string parameter = "int") =>
        $"public int M(int a) {{ return F(a); [System.Runtime.InteropServices.DllImport({import})] {returns} static extern int F({parameter} x); }}";

    private static BodyFingerprint Fingerprint(string member, string extra = "") => Fingerprint(Compile(member, extra: extra), legacy: false);

    private static BodyFingerprint Fingerprint(Compilation compilation, bool legacy) =>
        BodyFingerprinter.Compute(Method(compilation), compilation, EquivConfig.Default, legacy, Runtimes.Migration)!;

    private static string Text(Compilation compilation, string name = "M") => Text(compilation, Method(compilation, name));

    private static string Text(Compilation compilation, IMethodSymbol method) =>
        BodyFingerprinter.Text(method, compilation, EquivConfig.Default, [], Runtimes.Migration).Text!;

    private static IMethodSymbol Method(Compilation compilation, string name = "M") =>
        compilation.GetTypeByMetadataName("N.C")!.GetMembers(name).OfType<IMethodSymbol>().First();

    private static CSharpCompilation Compile(
        string members, LanguageVersion version = LanguageVersion.Preview, Platform platform = Platform.AnyCpu, string extra = "", bool partial = false, bool wrap = true, string header = "")
    {
        string source = wrap ? $"namespace N {{ {extra} public {(partial ? "abstract partial " : string.Empty)}class C {{ {members} }} }}" : $"namespace N {{ {members} }}";
        return CSharpCompilation.Create(
            "Snippet",
            [Parse(header + source, version)],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, platform: platform, allowUnsafe: true));
    }

    private static SyntaxTree Parse(string source, LanguageVersion version) =>
        CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(version), cancellationToken: TestContext.Current.CancellationToken);
}
