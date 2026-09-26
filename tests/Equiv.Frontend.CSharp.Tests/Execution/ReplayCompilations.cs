using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Small projects for the replay tests (ticket M4-009): compilations over the test host's own <c>System.Private.CoreLib</c>,
/// <c>System.Runtime</c> and <c>System.Console</c>, which is all a replay driver needs, and a pair built from a method of
/// each plus hand-written IR, so nothing here loads a solution or runs a driver.
/// </summary>
internal static class ReplayCompilations
{
    public static readonly ImmutableArray<MetadataReference> Runtime =
    [
        .. new[] { "System.Private.CoreLib.dll", "System.Runtime.dll", "System.Console.dll" }
            .Select(static name => MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, name))),
    ];

    public static CSharpCompilation Compile(string source, string assemblyName = "Project", params IEnumerable<MetadataReference> references) =>
        CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [.. Runtime, .. references],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>The one member named <paramref name="name"/> of the type <paramref name="type"/> names.</summary>
    public static IMethodSymbol Method(Compilation compilation, string type, string name) =>
        compilation.GetTypeByMetadataName(type)!.GetMembers(name).OfType<IMethodSymbol>().Single();

    /// <summary>A factory over one method per side, and the pair of <paramref name="oldIr"/> and <paramref name="newIr"/> it replays.</summary>
    public static (ReplayDriverFactory Factory, ProcedurePair Pair) Factory(
        Compilation legacy, IMethodSymbol legacyMethod, string oldIr, Compilation modern, IMethodSymbol modernMethod, string newIr)
    {
        ProcedureIdentity identity = new("N.C::M()");
        ReplayDriverFactory factory = new(
            new Dictionary<ProcedureIdentity, ReplayTarget> { [identity] = new(legacyMethod, legacy) },
            new Dictionary<ProcedureIdentity, ReplayTarget> { [identity] = new(modernMethod, modern) });
        return (factory, new ProcedurePair(identity, identity, IrText.Parse(oldIr), IrText.Parse(newIr)));
    }

    /// <summary>A counterexample over <paramref name="inputs"/> whose two runs end differently, as replay requires.</summary>
    public static Counterexample Counterexample(params IrValue[] inputs) =>
        new(new IrInputs([.. inputs]), new IrRun(new IrThrew("System.ArgumentNullException"), [], []), new IrRun(new IrThrew("System.NullReferenceException"), [], []));

    /// <summary>A <c>null.&lt;sort&gt;</c> map value that holds exactly <paramref name="nulls"/>.</summary>
    public static IrMapValue Nulls(string sort, params int[] nulls) => new(
        new IrMap(new IrSort(sort), new IrBool()),
        new IrBoolValue(Value: false),
        nulls.ToImmutableDictionary(n => (IrValue)new IrSortValue(sort, n), static _ => (IrValue)new IrBoolValue(Value: true)));
}
