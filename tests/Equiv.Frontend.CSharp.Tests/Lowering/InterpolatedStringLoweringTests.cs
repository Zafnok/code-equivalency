using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;
using static VerifyXunit.Verifier;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// An interpolated string whose holes are strings and integers, with no format or alignment clause, is the concatenation
/// of its parts (ticket P2-086): the closed <c>System.String::Concat(string,string)</c> calls <c>a + b</c> lowers to (ADR
/// 0041), an integer hole first formatted by its type's closed <c>ToString()</c>. That is what both a <c>string.Format</c>
/// binding and a <c>DefaultInterpolatedStringHandler</c> binding compute, so the same text is the same body under both.
/// Any other string stays opaque with reason <c>InterpolatedString</c>.
/// </summary>
public sealed class InterpolatedStringLoweringTests
{
    private const string Concat = "System.String::Concat(string,string)";

    /// <summary>Criterion 3: one text under the <c>string.Format</c> binding (C# 9) and the handler binding (C# 10) is one body.</summary>
    [Fact]
    public Task BothBindingsLowerToTheSameBody()
    {
        const string Members = "static string M(string s, int i, long n) => $\"a{s}b{i}c{n}\";";
        (IrProcedure formatted, BodyFingerprint formatPrint) = Bound(Members, LanguageVersion.CSharp9, legacy: true);
        (IrProcedure handled, BodyFingerprint handlerPrint) = Bound(Members, LanguageVersion.CSharp10, legacy: false);

        Assert.NotEqual(formatPrint, handlerPrint);
        Assert.Empty(Opaques(formatted));
        Assert.Equal(IrText.Dump(formatted), IrText.Dump(handled));
        return Verify(IrText.Dump(handled));
    }

    /// <summary>The holes are evaluated and joined in the order the <c>+</c> chain evaluates and joins its operands.</summary>
    [Theory]
    [InlineData("static string M(string s, string t) => $\"a{s}b{t}\";", "static string M(string s, string t) => \"a\" + s + \"b\" + t;")]
    [InlineData("static string M(string s, string t) => $\"{s}{t}\";", "static string M(string s, string t) => s + t;")]
    [InlineData("static string M(int i) => $\"n={i}\";", "static string M(int i) => \"n=\" + i.ToString();")]
    [InlineData("static string M(int i, long n) => $\"{i} of {n}\";", "static string M(int i, long n) => i.ToString() + \" of \" + n.ToString();")]
    [InlineData("static string F() => \"\"; static string M(int i, int k) => $\"{F()}{i}{k}\";", "static string F() => \"\"; static string M(int i, int k) => F() + i.ToString() + k.ToString();")]
    [InlineData("static int G() => 1; static string M(string s, int k) => $\"{s}{G()}{k}\";", "static int G() => 1; static string M(string s, int k) => s + G().ToString() + k.ToString();")]
    [InlineData(
        "const string K = \"k\"; string f; string M(int i, string s) { string l = s; return $\"{i}{s}{f}{K}{l}{7}\"; }",
        "const string K = \"k\"; string f; string M(int i, string s) { string l = s; return i.ToString() + s + f + K + l + 7.ToString(); }")]
    public void ACoveredStringLowersAsItsConcatenationDoes(string interpolated, string concatenation)
    {
        IrProcedure procedure = Method(interpolated);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(IrText.Dump(Method(concatenation)), IrText.Dump(procedure));
    }

    /// <summary><c>$"{s}"</c> is never null, as <c>"" + s</c> is not: a lone hole is joined to the empty string.</summary>
    [Theory]
    [InlineData("static string M(string s) => $\"{s}\";", "static string M(string s) => \"\" + s;")]
    [InlineData("static string M(int i) => $\"{i}\";", "static string M(int i) => \"\" + i.ToString();")]
    public void ALoneHoleIsJoinedToTheEmptyString(string interpolated, string concatenation)
    {
        IrProcedure procedure = Method(interpolated);

        Assert.Equal(Concat, Calls(procedure)[^1].Callee.Value);
        Assert.Equal(IrText.Dump(Method(concatenation)), IrText.Dump(procedure));
    }

    [Theory]
    [InlineData("sbyte", "System.SByte")]
    [InlineData("byte", "System.Byte")]
    [InlineData("short", "System.Int16")]
    [InlineData("ushort", "System.UInt16")]
    [InlineData("int", "System.Int32")]
    [InlineData("uint", "System.UInt32")]
    [InlineData("long", "System.Int64")]
    [InlineData("ulong", "System.UInt64")]
    public void AnIntegerHoleIsFormattedByItsClosedToString(string keyword, string type)
    {
        IrProcedure procedure = Method($"static string M({keyword} v) => $\"v={{v}}\";");

        Assert.Empty(Opaques(procedure));
        Assert.Equal([$"{type}::ToString()", Concat], Calls(procedure).Select(static c => c.Callee.Value), StringComparer.Ordinal);
        Assert.All(Calls(procedure), static c => Assert.True(c.Closed));
        Assert.Equal(new IrSort("System.String"), Calls(procedure)[0].Target!.Type);
    }

    /// <summary>
    /// A clause, a hole of any other type, and a hole after the first integer hole that is more than a read of a local, a
    /// parameter, a constant or a field of <c>this</c> (the handler formats the integer before that hole runs,
    /// <c>string.Format</c> after) keep the string opaque, and nothing in it is lowered.
    /// </summary>
    [Theory]
    [InlineData("static string M(int i) => $\"{i:D}\";")]
    [InlineData("static string M(string s) => $\"{s:x}\";")]
    [InlineData("static string M(int i) => $\"{i,5}\";")]
    [InlineData("static string M(string s) => $\"{s,5}\";")]
    [InlineData("static string M(string s, int i) => $\"{s}{i:D}\";")]
    [InlineData("static string M(double d) => $\"{d}\";")]
    [InlineData("static string M(float f) => $\"{f}\";")]
    [InlineData("static string M(decimal m) => $\"{m}\";")]
    [InlineData("static string M(bool b) => $\"{b}\";")]
    [InlineData("static string M(char c) => $\"{c}\";")]
    [InlineData("static string M(object o) => $\"{o}\";")]
    [InlineData("static string M(int? n) => $\"{n}\";")]
    [InlineData("static string M(DayOfWeek d) => $\"{d}\";")]
    [InlineData("static string M(nint p) => $\"{p}\";")]
    [InlineData("static string F() => \"\"; static string M(int i) => $\"{i}{F()}\";")]
    [InlineData("static int F() => 1; static string M(int i) => $\"{i}{F()}\";")]
    [InlineData("static string F() => \"\"; static string M(string s, int i, int k) => $\"{s}{i}{k}{F()}\";")]
    [InlineData("string P => \"\"; string M(int i) => $\"{i}{P}\";")]
    [InlineData("static string M(int i, string[] a) => $\"{i}{a[0]}\";")]
    [InlineData("static string g; static string M(int i) => $\"{i}{g}\";")]
    [InlineData("string f; static string M(int i, C o) => $\"{i}{o.f}\";")]
    [InlineData("static string M(int i, bool b, string s, string t) => $\"{i}{(b ? s : t)}\";")]
    public void AnyOtherStringStaysOpaque(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Equal("InterpolatedString", Assert.Single(Opaques(procedure)).Reason);
        Assert.Empty(Calls(procedure));
    }

    /// <summary>A hole with no type of its own, which the compiler converts to <c>string</c> under both bindings, is a string hole.</summary>
    [Theory]
    [InlineData("null", LanguageVersion.CSharp9)]
    [InlineData("null", LanguageVersion.CSharp10)]
    [InlineData("default", LanguageVersion.CSharp9)]
    [InlineData("default", LanguageVersion.CSharp10)]
    public void AHoleWithNoTypeOfItsOwnIsAStringHole(string hole, LanguageVersion version)
    {
        IrProcedure procedure = Bound($"static string M(string s) => $\"{{s}}{{{hole}}}\";", version, legacy: false).Body;

        Assert.Empty(Opaques(procedure));
        Assert.Equal(Concat, Assert.Single(Calls(procedure)).Callee.Value);
    }

    /// <summary>A string of constant strings is a constant under both bindings, as the compiler folds it.</summary>
    [Theory]
    [InlineData(LanguageVersion.CSharp9)]
    [InlineData(LanguageVersion.CSharp10)]
    public void AStringOfConstantsIsAConstant(LanguageVersion version)
    {
        IrProcedure procedure = Bound("const string A = \"x\"; static string M() => $\"{A}b\";", version, legacy: false).Body;

        Assert.Empty(Opaques(procedure));
        Assert.Empty(Calls(procedure));
    }

    private static (IrProcedure Body, BodyFingerprint Fingerprint) Bound(string members, LanguageVersion version, bool legacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText($"class C\n{{\n{members}\n}}\n", new CSharpParseOptions(version), cancellationToken: TestContext.Current.CancellationToken)],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration);
        Assert.Empty(IrValidator.Validate(procedure));
        return (procedure, BodyFingerprinter.Compute(method, compilation, EquivConfig.Default, legacy, Runtimes.Migration)!);
    }
}
