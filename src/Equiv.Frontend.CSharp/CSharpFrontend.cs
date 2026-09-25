using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Endpoints;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.Frontend.CSharp.Loading;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// <see cref="ILanguageFrontend"/> for C# (ARCHITECTURE.md): loads both sides through
/// <see cref="ISolutionLoader"/> (M2-001), enumerates procedures (M2-002), applies the config's
/// rename map plus the endpoint rename map <see cref="EndpointDiscovery"/> derives (M2-005), hands the
/// identity sets to <see cref="IProcedureMatcher"/>, lowers both bodies of every matched pair (M2-003), and counts
/// each side's analysed lines (<see cref="CodeLines"/>, M3-014). Each lowered pair carries both bodies' bound fingerprints
/// (<see cref="BodyFingerprinter"/>, M3-015). A pair whose lowering throws is a
/// <see cref="LoweringFailure"/>, not the end of the run (P2-011). The legacy body is lowered with the enabled
/// API-equivalence entries and the modern body with none; the pair lists the entries that fired (ADR 0020; M3-009).
/// </summary>
public sealed class CSharpFrontend : ILanguageFrontend
{
    private readonly ISolutionLoader _loader;
    private readonly IProcedureMatcher _matcher;
    private readonly Func<IMethodSymbol, Compilation, EquivConfig, bool, (IrProcedure Body, ImmutableArray<string> EquivalencesApplied)> _lower;

    public CSharpFrontend()
        : this(new MsBuildSolutionLoader(), new StableIdentityMatcher())
    {
    }

    /// <summary>Seam for unit tests: an <c>AdhocWorkspace</c>-backed <see cref="ISolutionLoader"/>.</summary>
    internal CSharpFrontend(ISolutionLoader loader, IProcedureMatcher matcher)
        : this(loader, matcher, LowerWithIrLowerer)
    {
    }

    /// <summary>Seam for unit tests: a <paramref name="lower"/> that can fault on a chosen procedure (P2-011).</summary>
    internal CSharpFrontend(
        ISolutionLoader loader,
        IProcedureMatcher matcher,
        Func<IMethodSymbol, Compilation, EquivConfig, bool, (IrProcedure Body, ImmutableArray<string> EquivalencesApplied)> lower)
    {
        _loader = loader;
        _matcher = matcher;
        _lower = lower;
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

        LoadedSolution legacy = SkipVacuousProjects(LoadOrThrow(legacyPath, ct));
        LoadedSolution modern = SkipVacuousProjects(LoadOrThrow(modernPath, ct));

        EndpointOverrides overrides = EndpointOverrides.Build(
            [.. legacy.Compilations.SelectMany(static compilation => EndpointDiscovery.Discover(compilation))],
            [.. modern.Compilations.SelectMany(static compilation => EndpointDiscovery.Discover(compilation))]);

        (ImmutableArray<SideProcedure> legacyProcedures, ImmutableArray<ProcedureIdentity> legacyAmbiguous) = Procedures(legacy.Compilations, config.Renames, overrides);
        (ImmutableArray<SideProcedure> modernProcedures, ImmutableArray<ProcedureIdentity> modernAmbiguous) = Procedures(modern.Compilations, config.Renames, overrides);

        MatchResult match = _matcher.Match(
            [.. legacyProcedures.Select(static p => p.Identity)],
            [.. modernProcedures.Select(static p => p.Identity)]);
        Dictionary<ProcedureIdentity, SideProcedure> legacyByIdentity = ByIdentity(legacyProcedures);
        Dictionary<ProcedureIdentity, SideProcedure> modernByIdentity = ByIdentity(modernProcedures);

        // ADR 0029: a procedure whose counterpart project, by assembly name, was skipped on the other side is unverified, not Added or Removed.
        (ImmutableArray<ProcedureIdentity> added, ImmutableArray<UnverifiedProject> legacySkipped) = Unverified(legacy.Skipped, match.Added, modernByIdentity, config.Renames);
        (ImmutableArray<ProcedureIdentity> removed, ImmutableArray<UnverifiedProject> modernSkipped) = Unverified(modern.Skipped, match.Removed, legacyByIdentity, config.Renames);

        (ImmutableArray<ProcedurePair> pairs, ImmutableArray<LoweringFailure> loweringFailures) = Lowered(match.Pairs, legacyByIdentity, modernByIdentity, config);
        MatchResult lowered = match with
        {
            Pairs = pairs,
            LoweringFailures = loweringFailures,
            Added = added,
            Removed = removed,
            Ambiguous = [.. match.Ambiguous, .. legacyAmbiguous, .. modernAmbiguous],
            LegacySkipped = legacySkipped,
            ModernSkipped = modernSkipped,
        };
        return new FrontendAnalysis(lowered, new AnalysedLines(CodeLines.Count(legacy.Compilations), CodeLines.Count(modern.Compilations)))
        {
            LegacyNotBuilt = legacy.NotBuilt,
            ModernNotBuilt = modern.NotBuilt,
        };
    }

    /// <summary>
    /// Both bodies of every pair in <paramref name="pairs"/>, lowered. A pair whose lowering throws is left out and
    /// returned as a <see cref="LoweringFailure"/> instead, so one bad body costs one pair (P2-011, ADR 0029's method
    /// level). <see cref="OperationCanceledException"/> and <see cref="OutOfMemoryException"/> propagate unchanged,
    /// as they do for verification (ADR 0023).
    /// </summary>
    private (ImmutableArray<ProcedurePair> Pairs, ImmutableArray<LoweringFailure> Failures) Lowered(
        ImmutableArray<ProcedurePair> pairs,
        Dictionary<ProcedureIdentity, SideProcedure> legacyByIdentity,
        Dictionary<ProcedureIdentity, SideProcedure> modernByIdentity,
        EquivConfig config)
    {
        ImmutableArray<ProcedurePair>.Builder lowered = ImmutableArray.CreateBuilder<ProcedurePair>(pairs.Length);
        ImmutableArray<LoweringFailure>.Builder failures = ImmutableArray.CreateBuilder<LoweringFailure>();
        foreach (ProcedurePair pair in pairs)
        {
            SideProcedure legacy = legacyByIdentity[pair.Old];
            SideProcedure modern = modernByIdentity[pair.New];
            try
            {
                (IrProcedure oldBody, ImmutableArray<string> oldApplied) = _lower(legacy.Symbol, legacy.Compilation, config, true);
                (IrProcedure newBody, ImmutableArray<string> newApplied) = _lower(modern.Symbol, modern.Compilation, config, false);
                lowered.Add(pair with
                {
                    OldBody = oldBody,
                    NewBody = newBody,
                    OldFingerprint = BodyFingerprinter.Compute(legacy.Symbol, legacy.Compilation, config, legacy: true),
                    NewFingerprint = BodyFingerprinter.Compute(modern.Symbol, modern.Compilation, config, legacy: false),
                    EquivalencesApplied = [.. oldApplied.Union(newApplied, StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                failures.Add(new LoweringFailure(pair.Old, pair.New, exception));
            }
        }

        return (lowered.ToImmutable(), failures.ToImmutable());
    }

    /// <summary>
    /// The production lowering (M2-003): the legacy side with the API-equivalence entries <c>equiv.config.json</c> does not
    /// suppress, the modern side with none (ADR 0020; ticket M3-009).
    /// </summary>
    internal static (IrProcedure Body, ImmutableArray<string> EquivalencesApplied) LowerWithIrLowerer(IMethodSymbol symbol, Compilation compilation, EquivConfig config, bool legacy) =>
        IrLowerer.Lower(
            symbol,
            compilation,
            config.Renames,
            config.SuppressRuntimeChanges,
            legacy ? ApiEquivalenceTable.Load().Enabled(config.SuppressApiEquivalences) : []);

    /// <summary>
    /// <paramref name="skipped"/> (one side's skipped projects) as Core data. Each C# project's procedures are its own,
    /// when it has a compilation to enumerate, plus the <paramref name="unmatched"/> identities from the other side whose
    /// assembly has its assembly name; those are taken out of <paramref name="unmatched"/>, which is returned as the remainder.
    /// </summary>
    private static (ImmutableArray<ProcedureIdentity> Remaining, ImmutableArray<UnverifiedProject> Skipped) Unverified(
        ImmutableArray<SkippedProject> skipped,
        ImmutableArray<ProcedureIdentity> unmatched,
        Dictionary<ProcedureIdentity, SideProcedure> otherSide,
        RenameMap renames)
    {
        HashSet<ProcedureIdentity> taken = [];
        ImmutableArray<UnverifiedProject>.Builder projects = ImmutableArray.CreateBuilder<UnverifiedProject>(skipped.Length);
        foreach (SkippedProject project in skipped)
        {
            IEnumerable<ProcedureIdentity> own = project.Compilation is { } compilation
                ? Procedures([compilation], renames, EndpointOverrides.None).Procedures.Select(static p => p.Identity)
                : [];
            IEnumerable<ProcedureIdentity> counterparts = project.IsCSharp
                ? unmatched.Where(identity => string.Equals(otherSide[identity].Compilation.AssemblyName, project.AssemblyName, StringComparison.Ordinal))
                : [];
            ImmutableArray<ProcedureIdentity> counterpartList = [.. counterparts];
            taken.UnionWith(counterpartList);
            projects.Add(new UnverifiedProject(
                project.Name,
                project.AssemblyName,
                project.IsCSharp,
                [.. project.Diagnostics.Select(static d => d.Id.Length > 0 ? $"{d.Id}: {d.Message}" : d.Message)],
                [.. own.Concat(counterpartList).Distinct()]));
        }

        return ([.. unmatched.Where(identity => !taken.Contains(identity))], projects.MoveToImmutable());
    }

    /// <summary>
    /// P2-018 (ADR 0029): after symbol enumeration, a loaded C# project that holds a type declaration but yields
    /// zero procedures is treated as a project the loader skipped, with reason
    /// <see cref="LoadDiagnosticKind.NoProcedures"/> ("a loaded project with source files that yields no procedures
    /// is a load failure, not an empty project"). A project with no type declaration at all (only assembly
    /// attributes, say) is unaffected: it was never going to yield procedures.
    /// </summary>
    private static LoadedSolution SkipVacuousProjects(LoadedSolution loaded)
    {
        ImmutableArray<Compilation>.Builder kept = ImmutableArray.CreateBuilder<Compilation>();
        ImmutableArray<SkippedProject>.Builder vacuous = ImmutableArray.CreateBuilder<SkippedProject>();
        foreach (Compilation compilation in loaded.Compilations)
        {
            if (!ProcedureEnumerator.Enumerate(compilation).IsEmpty || !DeclaresAType(compilation))
            {
                kept.Add(compilation);
                continue;
            }

            // A C# project's compilation always has an assembly name (it comes from the loaded Project).
            string assemblyName = compilation.AssemblyName!;
            LoadDiagnostic diagnostic = new(
                LoadDiagnosticKind.NoProcedures,
                string.Empty,
                assemblyName,
                "the project loaded and declares at least one type, but symbol enumeration found zero procedures");
            vacuous.Add(new SkippedProject(assemblyName, assemblyName, IsCSharp: true, [diagnostic], Compilation: null));
        }

        return vacuous.Count == 0
            ? loaded
            : loaded with { Compilations = kept.ToImmutable(), Skipped = [.. loaded.Skipped, .. vacuous] };
    }

    /// <summary>Whether any of the compilation's syntax trees declares a class, struct, interface, record or enum.</summary>
    private static bool DeclaresAType(Compilation compilation) =>
        compilation.SyntaxTrees.Any(static tree => tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Any());

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
        ImmutableArray<Compilation> compilations, RenameMap renames, EndpointOverrides overrides)
    {
        ImmutableArray<SideProcedure>.Builder procedures = ImmutableArray.CreateBuilder<SideProcedure>();
        ImmutableArray<ProcedureIdentity>.Builder forcedAmbiguous = ImmutableArray.CreateBuilder<ProcedureIdentity>();

        foreach (Compilation compilation in compilations)
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
        /// <summary>No endpoint overrides: for a skipped project, whose procedures are only listed.</summary>
        public static readonly EndpointOverrides None = new([], []);

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

    private sealed record SideProcedure(ProcedureIdentity Identity, IMethodSymbol Symbol, Compilation Compilation);
}
