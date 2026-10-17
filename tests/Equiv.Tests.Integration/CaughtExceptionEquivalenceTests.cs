using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-140 end to end: the exception a <c>catch</c> reads for its <c>Message</c> is a value both sides name, the
/// one the call that threw left or the object a <c>throw</c> in the same <c>try</c> made. Two sides that catch after the
/// same calls hold the same exception and are proved Equivalent; two that read the message of different exceptions are not.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CaughtExceptionEquivalenceTests
{
    private const string Helpers = "static void N() { } static void K() { } static void Log(string s) { } ";

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    [Theory]
    [InlineData(
        "static string M() { try { N(); return \"\"; } catch (Exception e) { return e.Message; } }",
        "static string M() { string r = \"\"; try { N(); } catch (Exception e) { r = e.Message; } return r; }")]
    [InlineData(
        "static void M() { try { N(); K(); } catch (InvalidOperationException e) { Log(e.Message); } }",
        "static void M() { try { N(); K(); } catch (InvalidOperationException failed) { string m = failed.Message; Log(m); } }")]
    [InlineData(
        "static string M(int a) { try { if (a > 0) throw new InvalidOperationException(); return \"\"; } catch (InvalidOperationException e) { return e.Message; } }",
        "static string M(int a) { try { if (a <= 0) return \"\"; throw new InvalidOperationException(); } catch (InvalidOperationException e) { return e.Message; } }")]
    public void TheSameExceptionReadForItsMessageIsEquivalent(string legacy, string modern) =>
        Assert.IsType<Equivalent>(Verify(legacy, modern));

    /// <summary>
    /// Acceptance criterion 3: both sides make the same calls, and where both of them threw one side reads the message of the
    /// exception <c>K</c> threw and the other that of the one <c>N</c> threw, so <c>get_Message</c> is handed different objects.
    /// </summary>
    [Fact]
    public void TheMessageOfAnotherExceptionIsDivergent() =>
        Assert.IsType<Divergent>(Verify(
            "static string M() { try { N(); } catch (Exception e) { try { K(); } catch (Exception f) { return f.Message; } return e.Message; } return \"\"; }",
            "static string M() { try { N(); } catch (Exception e) { try { K(); } catch (Exception) { return e.Message; } return e.Message; } return \"\"; }"));

    /// <summary>A thrown object against the exception a call threw, and the objects of two different <c>throw</c>s, are different exceptions.</summary>
    [Theory]
    [InlineData(
        "static string M() { try { N(); throw new InvalidOperationException(); } catch (Exception e) { return e.Message; } }",
        "static string M() { try { N(); N(); } catch (Exception e) { return e.Message; } return \"\"; }")]
    [InlineData(
        "static string M() { try { throw new InvalidOperationException(\"a\"); } catch (Exception e) { return e.Message; } }",
        "static string M() { try { throw new InvalidOperationException(\"b\"); } catch (Exception e) { return e.Message; } }")]
    public void AnotherExceptionIsNotEquivalent(string legacy, string modern) =>
        Assert.IsNotType<Equivalent>(Verify(legacy, modern));

    /// <summary>Ticket P2-180: two sides that hand the exception the same call threw to the same new exception are Equivalent.</summary>
    [Theory]
    [InlineData(
        "static string M() { try { N(); return \"\"; } catch (Exception e) { throw new InvalidOperationException(\"failed\", e); } }",
        "static string M() { string r = \"\"; try { N(); } catch (Exception failed) { throw new InvalidOperationException(\"failed\", failed); } return r; }")]
    [InlineData(
        "static void M() { try { N(); K(); } catch (InvalidOperationException e) { throw new ArgumentException(e.Message, e); } }",
        "static void M() { try { N(); K(); } catch (InvalidOperationException e) { string m = e.Message; throw new ArgumentException(m, e); } }")]
    public void TheSameInnerExceptionIsEquivalent(string legacy, string modern) =>
        Assert.IsType<Equivalent>(Verify(legacy, modern));

    /// <summary>
    /// Ticket P2-180: two sides that differ in what they do with the exception. One hands on the exception <c>K</c> threw and
    /// the other the one <c>N</c> threw; one hands it on and the other hands on none.
    /// </summary>
    [Theory]
    [InlineData(
        "static void M() { try { N(); } catch (Exception e) { try { K(); } catch (Exception f) { throw new InvalidOperationException(\"\", f); } } }",
        "static void M() { try { N(); } catch (Exception e) { try { K(); } catch (Exception) { throw new InvalidOperationException(\"\", e); } } }")]
    [InlineData(
        "static void M() { try { N(); } catch (Exception e) { throw new InvalidOperationException(\"failed\", e); } }",
        "static void M() { try { N(); } catch (Exception) { throw new InvalidOperationException(\"failed\", null); } }")]
    public void AnotherInnerExceptionIsDivergent(string legacy, string modern) =>
        Assert.IsType<Divergent>(Verify(legacy, modern));

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower($"class C {{ {Helpers}{legacy} }}", isLegacy: true), Lower($"class C {{ {Helpers}{modern} }}", isLegacy: false), Options);

    private static IrProcedure Lower(string source, bool isLegacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText("using System;\n" + source, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, Runtimes.Migration).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        Assert.DoesNotContain(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>(), static o => o.Reason is "CaughtException");
        return procedure;
    }
}
