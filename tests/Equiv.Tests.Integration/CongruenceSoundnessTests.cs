using System.Globalization;

using CsCheck;

using Equiv.Cli;
using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.TestSupport;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M3-015 acceptance criterion 7 (VERIFICATION-MODEL.md section 7's congruence obligation): whenever congruence
/// says Equivalent, the Z3 backend on the same pair never says Divergent. The pairs are <see cref="LoweringOracleGen"/>
/// methods set against themselves, against themselves reformatted with a comment, and against another generated method,
/// each side compiled on its own. CsCheck prints the seed on failure; <see cref="Seed"/> pins the run.
/// </summary>
public sealed class CongruenceSoundnessTests
{
    private const int Cases = 40;
    private const string Seed = "000000000000";

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, EquivConfig.Default.CallIdentityRenames);

    [Fact]
    public void CongruenceNeverContradictsTheSolver() =>
        Gen.Select(LoweringOracleGen.Method, LoweringOracleGen.Method, Gen.Int[0, 2]).Array[Cases]
            .Sample(static cases => Check(cases), seed: Seed, iter: 1, print: static cases => $"{cases.Length} cases");

    private static void Check((OracleMethod Legacy, OracleMethod Other, int Kind)[] cases)
    {
        OracleMethod[] modern = [.. cases.Select(static c => c.Kind switch
        {
            0 => c.Legacy,
            1 => c.Legacy with { Body = $"    // Reformatted.\n{c.Legacy.Body.Replace("    ", "\t", StringComparison.Ordinal)}" },
            _ => c.Other,
        })];
        CSharpCompilation legacyCompilation = Compile([.. cases.Select(static c => c.Legacy)]);
        CSharpCompilation modernCompilation = Compile(modern);
        INamedTypeSymbol legacyType = legacyCompilation.GetTypeByMetadataName("Oracle")!;
        INamedTypeSymbol modernType = modernCompilation.GetTypeByMetadataName("Oracle")!;
        int congruent = 0;
        int different = 0;
        for (int i = 0; i < cases.Length; i++)
        {
            string name = $"M{i.ToString(CultureInfo.InvariantCulture)}";
            ProcedurePair pair = Pair(legacyType.GetMembers(name).OfType<IMethodSymbol>().Single(), legacyCompilation, modernType.GetMembers(name).OfType<IMethodSymbol>().Single(), modernCompilation);
            if (!CompareCommand.IsCongruent(pair, pair.OldBody!, pair.NewBody!))
            {
                different += cases[i].Kind == 2 ? 1 : 0;
                continue;
            }

            congruent++;
            Verdict verdict = new Z3Backend().Verify(pair.OldBody!, pair.NewBody!, Options);
            Assert.False(verdict is Divergent, $"congruent but Divergent:\n{cases[i].Legacy.Render("M")}\n{modern[i].Render("M")}");
        }

        // The property is only evidence if congruence fired, and if it did not fire on every independent pair.
        int expected = cases.Count(static c => c.Kind < 2);
        Assert.True(congruent >= expected, string.Create(CultureInfo.InvariantCulture, $"{congruent} congruent pairs, {expected} identical up to trivia"));
        Assert.True(different > 0, "no independent pair was told apart");
    }

    private static ProcedurePair Pair(IMethodSymbol legacy, Compilation legacyCompilation, IMethodSymbol modern, Compilation modernCompilation)
    {
        ProcedureIdentity identity = new(legacy.Name);
        return new ProcedurePair(
            identity,
            identity,
            CSharpFrontend.LowerWithIrLowerer(legacy, legacyCompilation, EquivConfig.Default, legacy: true).Body,
            CSharpFrontend.LowerWithIrLowerer(modern, modernCompilation, EquivConfig.Default, legacy: false).Body)
        {
            OldFingerprint = BodyFingerprinter.Compute(legacy, legacyCompilation, EquivConfig.Default, legacy: true),
            NewFingerprint = BodyFingerprinter.Compute(modern, modernCompilation, EquivConfig.Default, legacy: false),
        };
    }

    private static CSharpCompilation Compile(OracleMethod[] methods)
    {
        string source = $"public static class Oracle\n{{\n{LoweringOracleGen.ClassMembers}{string.Concat(methods.Select(static (m, i) => m.Render($"M{i.ToString(CultureInfo.InvariantCulture)}")))}}}\n{LoweringOracleGen.CellSource}";
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Oracle",
            [CSharpSyntaxTree.ParseText(source, path: "Oracle.cs", cancellationToken: TestContext.Current.CancellationToken)],
            [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return compilation;
    }
}
