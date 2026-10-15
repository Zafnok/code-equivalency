using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Fingerprinting;

/// <summary>
/// Ticket P2-173 acceptance criterion 1: a pair that is not congruent names the first fingerprint component on which its
/// two sides differ, by the component's kind and the identity it concerns (ADR 0024 decision 1).
/// </summary>
public sealed class FingerprintDifferenceTests
{
    private const string Import = "[System.Runtime.InteropServices.DllImport(\"a.dll\")] ";

    private const string OneField = "public struct S { public int A; }";

    private const string TwoFields = "public struct S { public int A; public int B; }";

    private const string Equality = "public static bool operator ==(C a, C b) => true; public static bool operator !=(C a, C b) => false; ";

    private static SideRuntime OneRuntime => Runtimes.Between("net8.0", "net8.0");

    /// <summary>One case per component kind a bound text can differ in, each named by what it concerns and never by a value.</summary>
    [Theory]
    [InlineData("int M() => 1;", "long M() => 1;", FingerprintDifference.Signature, "method")]
    [InlineData("int M(int a, int b) { return a + b; }", "int M(int a, int b) { return a - b; }", FingerprintDifference.Operation, "Binary")]
    [InlineData("void M(int a) { _ = a + 1; }", "void M(int a) { _ = a + 1L; }", FingerprintDifference.Type, "System.Int32")]
    [InlineData("int M() => 1;", "int M() => 2;", FingerprintDifference.Constant, "Literal")]
    [InlineData("const int K = 1; int M() => K;", "const int K = 2; int M() => K;", FingerprintDifference.Constant, "N.C::K")]
    [InlineData("int x; int y; int M() => x;", "int x; int y; int M() => y;", FingerprintDifference.Symbol, "N.C::x")]
    [InlineData("bool M(C a, C b) => a == b;", Equality + "bool M(C a, C b) => a == b;", FingerprintDifference.Symbol, "N.C::op_Equality(N.C,N.C)")]
    [InlineData("int M(int a) { return a * 2; }", "int M(int a) { return checked(a * 2); }", FingerprintDifference.Context, "Binary")]
    [InlineData(Import + "private static extern int M();", "[System.Runtime.InteropServices.DllImport(\"b.dll\")] private static extern int M();", FingerprintDifference.Import, "method")]
    [InlineData(
        Import + "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)] private static extern bool M();",
        Import + "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] private static extern bool M();",
        FingerprintDifference.Attribute,
        "method return")]
    [InlineData(
        "private static partial int M(int a); private static partial int M(int a) { return a; }",
        "private static int M(int a) { return a; }",
        FingerprintDifference.BodyKind,
        "ImplementingPart")]
    [InlineData(
        "private static int M(int a) { return a; }",
        "private static partial int M(int a); private static partial int M(int a) { return a; }",
        FingerprintDifference.BodyKind,
        "ImplementingPart")]
    public void ADifferenceIsNamedByItsComponentAndWhatItConcerns(string legacy, string modern, string component, string identity) =>
        Assert.Equal(new FingerprintDifference(component, identity), Difference(Compile(legacy), Compile(modern)));

    [Fact]
    public void ACallThatBindsToAnotherOverloadIsNamedByItsCallee()
    {
        const string Caller = "int M(int a) => F.G(a);";
        Compilation legacy = Compile(Caller, "public static class F { public static int G(object o) => 0; }");
        Compilation modern = Compile(Caller, "public static class F { public static int G(object o) => 0; public static int G(int i) => 0; }");

        Assert.Equal(new FingerprintDifference(FingerprintDifference.Callee, "N.F::G(object)"), Difference(legacy, modern));
    }

    /// <summary>
    /// A type's declaration is named by the type, whichever side has the line the other lacks: a changed field, a field
    /// one side has after its last operation, and a field one side has where the other has its next operation.
    /// </summary>
    [Theory]
    [InlineData("unsafe int M() => sizeof(S);", OneField, "public struct S { public long A; }")]
    [InlineData("unsafe int M() => sizeof(S);", OneField, TwoFields)]
    [InlineData("unsafe int M() => sizeof(S);", TwoFields, OneField)]
    [InlineData("unsafe int M(int a) { int s = sizeof(S); return s + a; }", OneField, TwoFields)]
    [InlineData("unsafe int M(int a) { int s = sizeof(S); return s + a; }", TwoFields, OneField)]
    public void AnEditedTypeTheBodyReadsTheLayoutOfIsNamedByTheType(string member, string legacy, string modern) =>
        Assert.Equal(new FingerprintDifference(FingerprintDifference.TypeLayout, "N.S"), Difference(Compile(member, legacy), Compile(member, modern)));

    [Fact]
    public void AMarshallingAttributeOfTheAssemblyIsNamedByWhatCarriesIt()
    {
        const string Member = Import + "private static extern int M();";

        Assert.Equal(
            new FingerprintDifference(FingerprintDifference.Attribute, "method assembly"),
            Difference(Compile(Member), Compile(Member, header: "[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling] ")));
    }

    [Theory]
    [InlineData(false, true, "legacy")]
    [InlineData(true, false, "modern")]
    [InlineData(false, false, "legacy, modern")]
    public void ASideWithNoFingerprintIsNamed(bool old, bool @new, string sides)
    {
        BodyFingerprint fingerprint = new("ab", RuntimeSensitive: false);

        Assert.Equal(
            new FingerprintDifference(FingerprintDifference.NoFingerprint, sides),
            BodyFingerprinter.Difference(old ? fingerprint : null, @new ? fingerprint : null, NeverRead, NeverRead));
    }

    /// <summary>
    /// Ticket P2-181: equal texts are kept out of congruence by the runtime rules that apply to them, named each once in
    /// the order the text meets them, the legacy side's first: a callee a row flags, or a built-in rule.
    /// </summary>
    [Theory]
    [InlineData("bool M(char c) => char.IsLetterOrDigit(c) && char.IsLetterOrDigit(c);", "net48", false, "System.Char::IsLetterOrDigit(char)")]
    [InlineData("int M(double d) => (int)d;", "net8.0", false, BoundSerialiser.FloatToInteger)]
    [InlineData("double M(double d) => d + 1;", "net10.0", true, BoundSerialiser.X87)]
    [InlineData("double M(char c, double d) => char.IsLetterOrDigit(c) ? d : 0;", "net48", true, "x87, System.Char::IsLetterOrDigit(char)")]
    [InlineData("int M() { return F(); " + Import + "static extern int F(); }", "net48", false, BoundSerialiser.Extern)]
    [InlineData("int M() { return F(); " + Import + "static extern int F(); }", "net7.0", false, "extern:netcoredeps, extern:cet")]
    public void EqualTextsAreNamedByTheRuntimeRulesThatApply(string member, string legacy, bool x87, string rules)
    {
        Compilation compilation = Compile(member);
        IMethodSymbol method = Method(compilation);
        SideRuntime old = Runtimes.Between(legacy, "net10.0", x87);
        SideRuntime @new = Runtimes.Between(legacy, "net10.0");

        Assert.Equal(
            new FingerprintDifference(FingerprintDifference.RuntimeRule, rules),
            BodyFingerprinter.Difference(
                BodyFingerprinter.Compute(method, compilation, EquivConfig.Default, legacy: false, old),
                BodyFingerprinter.Compute(method, compilation, EquivConfig.Default, legacy: false, @new),
                () => BodyFingerprinter.Bound(method, compilation, EquivConfig.Default, legacy: false, old),
                () => BodyFingerprinter.Bound(method, compilation, EquivConfig.Default, legacy: false, @new)));
    }

    [Fact]
    public void ACongruentPairHasNoDifference()
    {
        BodyFingerprint fingerprint = new("ab", RuntimeSensitive: false);
        Compilation compilation = Compile("int M(int a) => a + 1;");
        ImmutableArray<BoundLine> lines = Lines(compilation);

        Assert.Null(BodyFingerprinter.Difference(fingerprint, fingerprint, NeverRead, NeverRead));
        Assert.Null(BoundLine.First(lines, Lines(Compile("int M(int b) => b + 1;"))));
    }

    /// <summary>The lines are the text: every field's value in order is what the fingerprint hashes, so equal lines are equal texts.</summary>
    [Theory]
    [InlineData("int M(int a) { int Twice(int x) => x * 2; return Twice(a) + F.G(a); }", "M")]
    [InlineData("public int P { get; set; }", "get_P")]
    [InlineData("private static partial int M(int a); [System.Obsolete] private static partial int M(int a) { return a; }", "M")]
    [InlineData(Import + "private static extern int M(S s, D d);", "M")]
    [InlineData("unsafe int M(E e) => sizeof(S) + sizeof(E);", "M")]
    public void TheLinesAreTheText(string member, string name)
    {
        const string Types =
            "public static class F { public static int G(int i) => 0; } public delegate int D([System.Runtime.InteropServices.In] int x); "
            + "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct S { [System.Obsolete] public int A; } "
            + "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] public struct E "
            + "{ [field: System.Runtime.InteropServices.FieldOffset(0)] public event System.Action Raised; }";
        Compilation compilation = Compile(member, Types);
        IMethodSymbol method = compilation.GetTypeByMetadataName("N.C")!.GetMembers(name).OfType<IMethodSymbol>().First();
        List<BoundLine> lines = [];

        string text = BodyFingerprinter.Text(method, compilation, EquivConfig.Default, [], OneRuntime, lines: lines).Text!;

        Assert.Equal(text, string.Concat(lines.SelectMany(static line => line.Fields).Select(static field => field.Value)));
        Assert.Equal(text.Count(static c => c == '\n'), lines.Count);
    }

    private static BoundText NeverRead() => throw new InvalidOperationException("the text is read only when the pair is not congruent");

    private static FingerprintDifference? Difference(Compilation legacy, Compilation modern) =>
        BodyFingerprinter.Difference(
            BodyFingerprinter.Compute(Method(legacy), legacy, EquivConfig.Default, legacy: true, OneRuntime),
            BodyFingerprinter.Compute(Method(modern), modern, EquivConfig.Default, legacy: false, OneRuntime),
            () => Bound(legacy),
            () => Bound(modern));

    private static ImmutableArray<BoundLine> Lines(Compilation compilation) => Bound(compilation).Lines;

    private static BoundText Bound(Compilation compilation) =>
        BodyFingerprinter.Bound(Method(compilation), compilation, EquivConfig.Default, legacy: false, OneRuntime);

    private static IMethodSymbol Method(Compilation compilation) =>
        compilation.GetTypeByMetadataName("N.C")!.GetMembers("M").OfType<IMethodSymbol>().First();

    private static CSharpCompilation Compile(string members, string extra = "", string header = "") =>
        CSharpCompilation.Create(
            "Snippet",
            [
                CSharpSyntaxTree.ParseText(
                    $"{header}namespace N {{ {extra} public abstract partial class C {{ {members} }} }}",
                    new CSharpParseOptions(LanguageVersion.Preview),
                    cancellationToken: TestContext.Current.CancellationToken),
            ],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
}
