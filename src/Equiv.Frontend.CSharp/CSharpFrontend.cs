using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Endpoints;
using Equiv.Frontend.CSharp.Loading;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// <see cref="ILanguageFrontend"/> for C# (ARCHITECTURE.md): loads both sides through
/// <see cref="ISolutionLoader"/> (M2-001), enumerates procedures (M2-002), applies the config's
/// rename map plus the endpoint rename map <see cref="EndpointDiscovery"/> derives (M2-005), hands the
/// identity sets to <see cref="IProcedureMatcher"/>, lowers both bodies of every matched pair (M2-003), and counts
/// each side's analysed lines (<see cref="CodeLines"/>, M3-014).
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

    public FrontendAnalysis Analyze(string legacyPath, string modernPath, EquivConfig config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);

        LoadedSolution legacy = LoadOrThrow(legacyPath, ct);
        LoadedSolution modern = LoadOrThrow(modernPath, ct);

        EndpointOverrides overrides = EndpointOverrides.Build(
            [.. legacy.Compilations.SelectMany(static compilation => EndpointDiscovery.Discover(compilation))],
            [.. modern.Compilations.SelectMany(static compilation => EndpointDiscovery.Discover(compilation))]);

        (ImmutableArray<SideProcedure> legacyProcedures, ImmutableArray<ProcedureIdentity> legacyAmbiguous) = Procedures(legacy, config.Renames, overrides);
        (ImmutableArray<SideProcedure> modernProcedures, ImmutableArray<ProcedureIdentity> modernAmbiguous) = Procedures(modern, config.Renames, overrides);

        MatchResult match = _matcher.Match(
            [.. legacyProcedures.Select(static p => p.Identity)],
            [.. modernProcedures.Select(static p => p.Identity)]);
        Dictionary<ProcedureIdentity, SideProcedure> legacyByIdentity = ByIdentity(legacyProcedures);
        Dictionary<ProcedureIdentity, SideProcedure> modernByIdentity = ByIdentity(modernProcedures);

        MatchResult lowered = match with
        {
            Pairs = [.. match.Pairs.Select(pair => pair with
            {
                OldBody = legacyByIdentity[pair.Old].Lower(config),
                NewBody = modernByIdentity[pair.New].Lower(config),
            })],
            Ambiguous = [.. match.Ambiguous, .. legacyAmbiguous, .. modernAmbiguous],
        };
        return new FrontendAnalysis(lowered, new AnalysedLines(CodeLines.Count(legacy.Compilations), CodeLines.Count(modern.Compilations)));
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
    /// <see cref="EnumeratedProcedure.Location"/>, plus what M2-003 needs to lower the body. An action
    /// <paramref name="overrides"/> maps to its endpoint identity instead, unless <paramref name="renames"/>
    /// already changes that action's own identity (M2-005 acceptance criterion 3: the user's rename map
    /// wins on conflict). An action <paramref name="overrides"/> flags ambiguous is excluded here and
    /// returned separately with its location attached, for <see cref="Analyze"/> to fold into
    /// <see cref="MatchResult.Ambiguous"/> instead of matching it by plain identity.
    /// </summary>
    private static (ImmutableArray<SideProcedure> Procedures, ImmutableArray<ProcedureIdentity> ForcedAmbiguous) Procedures(
        LoadedSolution solution, RenameMap renames, EndpointOverrides overrides)
    {
        ImmutableArray<SideProcedure>.Builder procedures = ImmutableArray.CreateBuilder<SideProcedure>();
        ImmutableArray<ProcedureIdentity>.Builder forcedAmbiguous = ImmutableArray.CreateBuilder<ProcedureIdentity>();

        foreach (Compilation compilation in solution.Compilations)
        {
            foreach (EnumeratedProcedure procedure in ProcedureEnumerator.Enumerate(compilation))
            {
                SourceSpan span = ToSourceSpan(procedure.Location);
                if (overrides.ForcedAmbiguous.Contains(procedure.Identity))
                {
                    forcedAmbiguous.Add(procedure.Identity with { Location = span });
                    continue;
                }

                ProcedureIdentity renamed = RoslynIdentity.Of(procedure.Symbol, renames);
                ProcedureIdentity identity = renamed == procedure.Identity && overrides.RenameTo.TryGetValue(procedure.Identity, out ProcedureIdentity? endpointIdentity)
                    ? endpointIdentity
                    : renamed;
                procedures.Add(new SideProcedure(identity with { Location = span }, procedure.Symbol, compilation));
            }
        }

        return (procedures.ToImmutable(), forcedAmbiguous.ToImmutable());
    }

    /// <summary>
    /// The endpoint rename map M2-005 acceptance criterion 3 describes, built from both sides'
    /// <see cref="Endpoint"/>s before matching: a <c>(Verb, Template)</c> present on exactly one action
    /// per side maps both of those actions' raw identities to the one shared endpoint identity
    /// (<see cref="ProcedureIdentityNormalizer.Endpoint"/>), so they match by plain equality and the
    /// resulting pair's identity is already the <c>"VERB /template"</c> string
    /// <see cref="Equiv.Core.Reporting.SarifReportWriter"/> needs for a <c>logicalLocations</c> entry
    /// (criterion 4).
    /// A <c>(Verb, Template)</c> with duplicates on one side but present on both is left out of the
    /// rename map entirely and every action sharing it (both sides) lands in <see cref="ForcedAmbiguous"/>.
    /// A <c>(Verb, Template)</c> present on only one side is left alone: there is nothing to conflict
    /// with, so it falls through to ordinary Added/Removed handling by plain identity.
    /// </summary>
    private sealed record EndpointOverrides(
        ImmutableDictionary<ProcedureIdentity, ProcedureIdentity> RenameTo,
        ImmutableHashSet<ProcedureIdentity> ForcedAmbiguous)
    {
        public static EndpointOverrides Build(ImmutableArray<Endpoint> legacyEndpoints, ImmutableArray<Endpoint> modernEndpoints)
        {
            ILookup<(string Verb, string Template), Endpoint> legacyByKey = legacyEndpoints.ToLookup(static e => (e.Verb, e.Template));
            ILookup<(string Verb, string Template), Endpoint> modernByKey = modernEndpoints.ToLookup(static e => (e.Verb, e.Template));

            Dictionary<ProcedureIdentity, ProcedureIdentity> renameTo = [];
            HashSet<ProcedureIdentity> forcedAmbiguous = [];

            IEnumerable<(string Verb, string Template)> keys =
                legacyByKey.Select(static g => g.Key).Concat(modernByKey.Select(static g => g.Key)).Distinct();
            foreach ((string Verb, string Template) key in keys)
            {
                ImmutableArray<Endpoint> legacyGroup = [.. legacyByKey[key]];
                ImmutableArray<Endpoint> modernGroup = [.. modernByKey[key]];
                if (legacyGroup.IsEmpty || modernGroup.IsEmpty)
                {
                    continue;
                }

                if (legacyGroup.Length == 1 && modernGroup.Length == 1)
                {
                    ProcedureIdentity shared = ProcedureIdentityNormalizer.Endpoint(key.Verb, key.Template);
                    renameTo[legacyGroup[0].Action] = shared;
                    renameTo[modernGroup[0].Action] = shared;
                }
                else
                {
                    foreach (Endpoint endpoint in legacyGroup.Concat(modernGroup))
                    {
                        forcedAmbiguous.Add(endpoint.Action);
                    }
                }
            }

            return new EndpointOverrides(renameTo.ToImmutableDictionary(), [.. forcedAmbiguous]);
        }
    }

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
        public IrProcedure Lower(EquivConfig config) => IrLowerer.Lower(Symbol, Compilation, config.Renames, config.SuppressRuntimeChanges);
    }
}
