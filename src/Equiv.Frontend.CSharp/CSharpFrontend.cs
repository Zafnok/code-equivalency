using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Loading;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// <see cref="ILanguageFrontend"/> for C# (ARCHITECTURE.md): loads both sides through
/// <see cref="ISolutionLoader"/> (M2-001), enumerates procedures (M2-002), applies the config's
/// rename map, hands the identity sets to <see cref="IProcedureMatcher"/>, and lowers both bodies of
/// every matched pair (M2-003).
/// </summary>
public sealed class CSharpFrontend : ILanguageFrontend
{
    private readonly ISolutionLoader _loader;
    private readonly IProcedureMatcher _matcher;

    public CSharpFrontend()
        : this(new MsBuildSolutionLoader(), new StableIdentityMatcher())
    {
    }

    /// <summary>Seam for unit tests: an <c>AdhocWorkspace</c>-backed <see cref="ISolutionLoader"/>.</summary>
    internal CSharpFrontend(ISolutionLoader loader, IProcedureMatcher matcher)
    {
        _loader = loader;
        _matcher = matcher;
    }

    public string Language => "csharp";

    public bool Supports(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    public MatchResult Analyze(string legacyPath, string modernPath, EquivConfig config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);

        LoadedSolution legacy = LoadOrThrow(legacyPath, ct);
        LoadedSolution modern = LoadOrThrow(modernPath, ct);

        ImmutableArray<SideProcedure> legacyProcedures = Procedures(legacy, config.Renames);
        ImmutableArray<SideProcedure> modernProcedures = Procedures(modern, config.Renames);

        MatchResult match = _matcher.Match(
            [.. legacyProcedures.Select(static p => p.Identity)],
            [.. modernProcedures.Select(static p => p.Identity)]);
        Dictionary<ProcedureIdentity, SideProcedure> legacyByIdentity = ByIdentity(legacyProcedures);
        Dictionary<ProcedureIdentity, SideProcedure> modernByIdentity = ByIdentity(modernProcedures);

        return match with
        {
            Pairs = [.. match.Pairs.Select(pair => pair with
            {
                OldBody = legacyByIdentity[pair.Old].Lower(config.Renames),
                NewBody = modernByIdentity[pair.New].Lower(config.Renames),
            })],
        };
    }

    private LoadedSolution LoadOrThrow(string path, CancellationToken ct)
    {
        try
        {
            return _loader.LoadAsync(path, ct).GetAwaiter().GetResult();
        }
        catch (SolutionLoadException exception)
        {
            throw new FrontendLoadException(path, exception.Message);
        }
    }

    /// <summary>
    /// Rename-applied identities for a loaded side (M2-002 acceptance criterion 3's "apply the config
    /// rename map"), each carrying the declaring file/line (M2-002 acceptance criterion 5) converted from
    /// <see cref="EnumeratedProcedure.Location"/>, plus what M2-003 needs to lower the body.
    /// </summary>
    private static ImmutableArray<SideProcedure> Procedures(LoadedSolution solution, RenameMap renames) =>
        [.. solution.Compilations
            .SelectMany(static compilation => ProcedureEnumerator.Enumerate(compilation).Select(procedure => (compilation, procedure)))
            .Select(item => new SideProcedure(
                RoslynIdentity.Of(item.procedure.Symbol, renames) with { Location = ToSourceSpan(item.procedure.Location) },
                item.procedure.Symbol,
                item.compilation))];

    /// <summary>First occurrence wins; only identities unique on a side are ever paired, so pairs never see the others.</summary>
    private static Dictionary<ProcedureIdentity, SideProcedure> ByIdentity(ImmutableArray<SideProcedure> procedures)
    {
        Dictionary<ProcedureIdentity, SideProcedure> byIdentity = [];
        foreach (SideProcedure procedure in procedures)
        {
            byIdentity.TryAdd(procedure.Identity, procedure);
        }

        return byIdentity;
    }

    internal static SourceSpan ToSourceSpan(Location location)
    {
        FileLinePositionSpan span = location.GetLineSpan();
        return new SourceSpan(
            span.Path,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            span.EndLinePosition.Character + 1);
    }

    private sealed record SideProcedure(ProcedureIdentity Identity, IMethodSymbol Symbol, Compilation Compilation)
    {
        public IrProcedure Lower(RenameMap renames) => IrLowerer.Lower(Symbol, Compilation, renames);
    }
}
