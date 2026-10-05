using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.RuntimeChanges;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp.Endpoints;
using Equiv.Frontend.CSharp.Execution;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.Frontend.CSharp.Loading;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;

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
/// API-equivalence entries and the modern body with none; the pair lists the entries that fired (ADR 0020; M3-009). The
/// analysis carries a <see cref="ReplayDriverFactory"/> over the loaded projects, which emits nothing until a replay asks
/// (ticket M4-009). Each loaded project gets its runtime (<see cref="RuntimeDetection"/>, ADR 0040; P2-053), and each pair is
/// lowered and fingerprinted with the interval between its two projects' runtimes (<see cref="SideRuntime"/>; P2-055), which
/// the pair carries as <see cref="ProcedurePair.Runtimes"/>. Under
/// <c>--il-fallback</c> a pair may keep bodies lowered from IL instead (<see cref="IlFallback"/>, ADR 0039; P1-016). A pair
/// with a call site that has the same text on both sides and binds to a different callee is lowered again with each such
/// call an opaque, and lists the callee pairs (<see cref="CallSites"/>, ADR 0042; P2-069).
/// </summary>
public sealed class CSharpFrontend : ILanguageFrontend
{
    private readonly ISolutionLoader _loader;
    private readonly IProcedureMatcher _matcher;
    private readonly Func<IMethodSymbol, Compilation, EquivConfig, bool, SideRuntime, CallSites, (IrProcedure Body, ImmutableArray<string> EquivalencesApplied)> _lower;

    public CSharpFrontend()
        : this(CreateLoader(OperatingSystem.IsWindows()), new StableIdentityMatcher())
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
        Func<IMethodSymbol, Compilation, EquivConfig, bool, SideRuntime, CallSites, (IrProcedure Body, ImmutableArray<string> EquivalencesApplied)> lower)
    {
        _loader = loader;
        _matcher = matcher;
        _lower = lower;
    }

    public string Language => "csharp";

    /// <summary>
    /// The loader for this OS (ADR 0031, M3-029): MSBuildWorkspace on Windows, where VS Build Tools load non-SDK projects;
    /// elsewhere the composite loader, which gives non-SDK projects to the bare loader.
    /// </summary>
    internal static ISolutionLoader CreateLoader(bool isWindows) => isWindows ? new MsBuildSolutionLoader() : new CompositeSolutionLoader();

    public bool Supports(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    public FrontendAnalysis Analyze(string legacyPath, string modernPath, EquivConfig config, IRunLog log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(log);

        LoadedSolution legacy = Loaded("load-legacy", legacyPath, Codebase.Legacy, log, ct);
        legacy = legacy with { Runtimes = RuntimeDetection.Detect(legacy.Compilations, config.LegacyRuntime) };
        LoadedSolution modern = Loaded("load-modern", modernPath, Codebase.Modern, log, ct);
        modern = modern with { Runtimes = RuntimeDetection.Detect(modern.Compilations, config.ModernRuntime) };

        log.Phase("enumerate", 2, legacy.Compilations.Length + modern.Compilations.Length);
        EndpointOverrides overrides = EndpointOverrides.Build(Endpoints(legacy.Compilations), Endpoints(modern.Compilations));

        (ImmutableArray<SideProcedure> legacyProcedures, ImmutableArray<ProcedureIdentity> legacyAmbiguous) = Enumerated("legacy", legacy, config, overrides, log);
        (ImmutableArray<SideProcedure> modernProcedures, ImmutableArray<ProcedureIdentity> modernAmbiguous) = Enumerated("modern", modern, config, overrides, log);
        log.PhaseDone();

        log.Phase("match", 1, legacyProcedures.Length + modernProcedures.Length);
        log.Item("procedures", legacyProcedures.Length + modernProcedures.Length);
        MatchResult match = _matcher.Match(
            [.. legacyProcedures.Select(static p => p.Identity)],
            [.. modernProcedures.Select(static p => p.Identity)]);
        log.ItemDone($"{match.Pairs.Length} pairs");
        log.PhaseDone();
        Dictionary<ProcedureIdentity, SideProcedure> legacyByIdentity = ByIdentity(legacyProcedures);
        Dictionary<ProcedureIdentity, SideProcedure> modernByIdentity = ByIdentity(modernProcedures);

        // ADR 0029: a procedure whose counterpart project, by assembly name, was skipped on the other side is unverified, not Added or Removed.
        (ImmutableArray<ProcedureIdentity> added, ImmutableArray<UnverifiedProject> legacySkipped) = Unverified(legacy.Skipped, match.Added, modernByIdentity, config.Renames);
        (ImmutableArray<ProcedureIdentity> removed, ImmutableArray<UnverifiedProject> modernSkipped) = Unverified(modern.Skipped, match.Removed, legacyByIdentity, config.Renames);

        Sides sides = new(legacyByIdentity, ByProject(legacy.Runtimes), modernByIdentity, ByProject(modern.Runtimes));
        (ImmutableArray<ProcedurePair> pairs, ImmutableArray<LoweringFailure> loweringFailures) = Lowered(match.Pairs, sides, config, log);
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
            LegacyRuntimes = Reported(legacy.Runtimes),
            ModernRuntimes = Reported(modern.Runtimes),
            Replay = new ReplayDriverFactory(Targets(legacyByIdentity, legacy.Runtimes), Targets(modernByIdentity, modern.Runtimes), DriverReferences.Installed().Host),
        };
    }

    /// <summary>A side's project runtimes as <see cref="FrontendAnalysis"/> reports them (P2-053).</summary>
    private static ImmutableArray<(string Project, string Runtime, string Source)> Reported(ImmutableArray<ProjectRuntime> runtimes) =>
        [.. runtimes.Select(static r => (r.Project, r.Runtime, r.Source))];

    /// <summary>
    /// Each procedure's symbol, compilation and its project's runtime, for replay under <c>--execute</c> (tickets M4-009,
    /// P2-056). A project hosted on several runtimes runs on the first in runtime order, as <c>run.properties.runtimes</c>
    /// lists them; an unhosted one has none.
    /// </summary>
    private static Dictionary<ProcedureIdentity, ReplayTarget> Targets(Dictionary<ProcedureIdentity, SideProcedure> byIdentity, ImmutableArray<ProjectRuntime> runtimes)
    {
        Dictionary<string, TargetRuntime?> byProject = runtimes.DistinctBy(static r => r.Project, StringComparer.Ordinal)
            .ToDictionary(static r => r.Project, static r => r.Runtimes.FirstOrDefault(), StringComparer.Ordinal);
        return byIdentity.ToDictionary(
            static p => p.Key,
            p => new ReplayTarget(p.Value.Symbol, p.Value.Compilation, byProject.GetValueOrDefault(p.Value.Compilation.AssemblyName!)));
    }

    /// <summary>
    /// Both bodies of every pair in <paramref name="pairs"/>, lowered. A pair whose lowering throws is left out and
    /// returned as a <see cref="LoweringFailure"/> instead, so one bad body costs one pair (P2-011, ADR 0029's method
    /// level). <see cref="OperationCanceledException"/> and <see cref="OutOfMemoryException"/> propagate unchanged,
    /// as they do for verification (ADR 0023). Both bodies are lowered and fingerprinted with the one interval between the
    /// runtimes of the two projects they come from (ADR 0040 decision 2; P2-055), so a runtime rule the pair does not cross
    /// applies to neither. A matched method whose two sides do not agree on what it forwards to is kept as a callee in every
    /// body of the run (ADR 0047; ticket P2-068).
    /// </summary>
    private (ImmutableArray<ProcedurePair> Pairs, ImmutableArray<LoweringFailure> Failures) Lowered(
        ImmutableArray<ProcedurePair> pairs,
        Sides sides,
        EquivConfig config,
        IRunLog log)
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        ImmutableHashSet<string> kept = KeptForwarders(pairs, sides, config.Renames);
        log.Phase("lower", pairs.Length, pairs.Length);
        ImmutableArray<ProcedurePair>.Builder lowered = ImmutableArray.CreateBuilder<ProcedurePair>(pairs.Length);
        ImmutableArray<LoweringFailure>.Builder failures = ImmutableArray.CreateBuilder<LoweringFailure>();
        foreach (ProcedurePair pair in pairs)
        {
            SideProcedure legacy = sides.Legacy[pair.Old];
            SideProcedure modern = sides.Modern[pair.New];
            (SideRuntime legacyRuntime, SideRuntime modernRuntime) = SideRuntime.Of(
                sides.LegacyRuntimes[legacy.Compilation.AssemblyName!], legacy.Compilation, sides.ModernRuntimes[modern.Compilation.AssemblyName!], modern.Compilation, table);
            log.Item(pair.New.Value, 1);
            try
            {
                (IrProcedure oldBody, IrProcedure newBody, ImmutableArray<string> applied, ImmutableArray<ReboundCall> rebound, ImmutableArray<ResolvedForwarder> forwarders) =
                    Bodies((legacy, legacyRuntime), (modern, modernRuntime), config, kept);
                lowered.Add(Relowered(
                    pair with
                    {
                        OldBody = oldBody,
                        NewBody = newBody,
                        OldFingerprint = BodyFingerprinter.Compute(legacy.Symbol, legacy.Compilation, config, legacy: true, legacyRuntime, kept),
                        NewFingerprint = BodyFingerprinter.Compute(modern.Symbol, modern.Compilation, config, legacy: false, modernRuntime, kept),
                        EquivalencesApplied = applied,
                        ReboundCalls = rebound,
                        ForwardersResolved = forwarders,
                        Runtimes = legacyRuntime.Interval,
                    },
                    (legacy, legacyRuntime),
                    (modern, modernRuntime),
                    config,
                    kept,
                    log));
                log.ItemDone("lowered");
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                failures.Add(new LoweringFailure(pair.Old, pair.New, exception));
                log.ItemDone("failed");
            }
        }

        log.PhaseDone();
        return (lowered.ToImmutable(), failures.ToImmutable());
    }

    /// <summary>
    /// The identities of the matched methods a call to which is not the same call on both sides (ADR 0047; ticket P2-068):
    /// one side's is a forwarder and the other's is not, or the two forward to different callees. No body resolves such a
    /// forwarder, so its callers still assume the pair (ADR 0019) and its own result says what changed.
    /// </summary>
    private static ImmutableHashSet<string> KeptForwarders(ImmutableArray<ProcedurePair> pairs, Sides sides, RenameMap renames) =>
        pairs.Select(p => (Legacy: sides.Legacy[p.Old], Modern: sides.Modern[p.New]))
            .Where(p => !Forwarders.Agree((p.Legacy.Symbol, p.Legacy.Compilation), (p.Modern.Symbol, p.Modern.Compilation), renames))
            .SelectMany(p => (string[])[CallIdentityFactory.Name(p.Legacy.Symbol, renames), CallIdentityFactory.Name(p.Modern.Symbol, renames)])
            .ToImmutableHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Both bodies of a pair, the catalogue entries that fired in either, sorted, the pair's rebound calls (ADR 0042;
    /// ticket P2-069) and the forwarders the bodies it returns resolved (ADR 0047; ticket P2-068). The bodies are lowered once to record their call sites. When a site with the same text binds to a
    /// different callee on each side, both are lowered again with every call to such a callee an opaque. Every lowering of a
    /// side uses that side's runtime facts (ADR 0040; ticket P2-055).
    /// </summary>
    private (IrProcedure Old, IrProcedure New, ImmutableArray<string> Applied, ImmutableArray<ReboundCall> Rebound, ImmutableArray<ResolvedForwarder> Forwarders) Bodies(
        (SideProcedure Procedure, SideRuntime Runtime) legacySide, (SideProcedure Procedure, SideRuntime Runtime) modernSide, EquivConfig config, ImmutableHashSet<string> kept)
    {
        (SideProcedure legacy, SideRuntime legacyRuntime) = legacySide;
        (SideProcedure modern, SideRuntime modernRuntime) = modernSide;
        CallSites oldSites = new() { KeptForwarders = kept };
        CallSites newSites = new() { KeptForwarders = kept };
        (IrProcedure oldBody, ImmutableArray<string> oldApplied) = _lower(legacy.Symbol, legacy.Compilation, config, true, legacyRuntime, oldSites);
        (IrProcedure newBody, ImmutableArray<string> newApplied) = _lower(modern.Symbol, modern.Compilation, config, false, modernRuntime, newSites);
        ImmutableArray<ReboundCall> rebound = CallSites.Rebound(oldSites, newSites, config.CallIdentityRenames);
        if (!rebound.IsEmpty)
        {
            oldSites = new CallSites(rebound.Select(static r => r.Legacy)) { KeptForwarders = kept };
            newSites = new CallSites(rebound.Select(static r => r.Modern)) { KeptForwarders = kept };
            (oldBody, oldApplied) = _lower(legacy.Symbol, legacy.Compilation, config, true, legacyRuntime, oldSites);
            (newBody, newApplied) = _lower(modern.Symbol, modern.Compilation, config, false, modernRuntime, newSites);
        }

        return (oldBody, newBody, [.. oldApplied.Union(newApplied, StringComparer.Ordinal).Order(StringComparer.Ordinal)], rebound, CallSites.Forwarders(oldSites, newSites));
    }

    /// <summary>
    /// <paramref name="pair"/>, lowered from IOperation and fingerprinted, with the bodies it keeps. Under <c>--il-fallback</c>
    /// <see cref="IlFallback"/> may lower both sides again from IL, once congruence is decided on the fingerprints (ADR 0039;
    /// ticket P1-016); IL-lowered bodies applied no API equivalence, and resolved the forwarders their own calls name (ADR
    /// 0047; ticket P2-068). In thorough mode the IL bodies never replace the IOperation ones: a pair <see cref="IlFallback"/>
    /// would have given them to keeps both, the IL ones as <see cref="ProcedurePair.Il"/> for the IL pass, with or without
    /// the flag (ADR 0049 decision 2; ticket P1-032). Then, when exactly one side is <c>async</c>, both bodies
    /// are one opaque, whichever lowering they came from (ticket M4-006); such a pair has no IL bodies, since an
    /// <c>async</c> method's IL is not read.
    /// </summary>
    private static ProcedurePair Relowered(
        ProcedurePair pair, (SideProcedure Procedure, SideRuntime Runtime) legacySide, (SideProcedure Procedure, SideRuntime Runtime) modernSide, EquivConfig config, ImmutableHashSet<string> kept, IRunLog log)
    {
        SideProcedure legacy = legacySide.Procedure;
        SideProcedure modern = modernSide.Procedure;
        bool thorough = config.Mode == CompareMode.Thorough;
        if (thorough || config.IlFallback)
        {
            bool congruent = pair.OldFingerprint is { RuntimeSensitive: false } fingerprint && fingerprint == pair.NewFingerprint;
            CallSites oldSites = new(pair.ReboundCalls.Select(static r => r.Legacy)) { KeptForwarders = kept };
            CallSites newSites = new(pair.ReboundCalls.Select(static r => r.Modern)) { KeptForwarders = kept };
            (IrProcedure old, IrProcedure @new, bool tried, string lowering) = IlFallback.Choose(
                new IlFallback.Side(legacy.Symbol, legacy.Compilation, pair.OldBody!, legacySide.Runtime) { Sites = oldSites },
                new IlFallback.Side(modern.Symbol, modern.Compilation, pair.NewBody!, modernSide.Runtime) { Sites = newSites },
                congruent,
                log);
            bool il = string.Equals(lowering, IlFallback.Il, StringComparison.Ordinal);
            pair = thorough
                ? pair with { Il = il ? new IlBodies(old, @new, CallSites.Forwarders(oldSites, newSites)) : null }
                : pair with
                {
                    OldBody = old,
                    NewBody = @new,
                    Lowering = lowering,
                    IlFallbackTried = tried,
                    EquivalencesApplied = il ? [] : pair.EquivalencesApplied,
                    ForwardersResolved = il ? CallSites.Forwarders(oldSites, newSites) : pair.ForwardersResolved,
                };
        }

        // Ticket M4-006: a sync method throws to its caller and an async one into its task, so the pair is decided from the
        // two signatures, before either body: both are one opaque the CLI makes Unknown without the solver.
        return legacy.Symbol.IsAsync == modern.Symbol.IsAsync
            ? pair
            : pair with
            {
                OldBody = IrLowerer.Opaque(pair.OldBody!, Unknown.AsyncMismatchReason, ToSourceSpan(legacy.Symbol.Locations[0])),
                NewBody = IrLowerer.Opaque(pair.NewBody!, Unknown.AsyncMismatchReason, ToSourceSpan(modern.Symbol.Locations[0])),
            };
    }

    /// <summary>
    /// The production lowering (M2-003): the legacy side with the API-equivalence entries <c>equiv.config.json</c> does not
    /// suppress, the modern side with none (ADR 0020; ticket M3-009), and each side with the runtime rules that apply
    /// inside the pair's interval (ADRs 0025, 0040; tickets M4-002, P2-055). <paramref name="sites"/> records the body's
    /// call sites and holds the identities whose calls are rebound (ADR 0042; ticket P2-069).
    /// </summary>
    internal static (IrProcedure Body, ImmutableArray<string> EquivalencesApplied) LowerWithIrLowerer(
        IMethodSymbol symbol, Compilation compilation, EquivConfig config, bool legacy, SideRuntime runtime, CallSites? sites = null) =>
        IrLowerer.Lower(
            symbol,
            compilation,
            config.Renames,
            config.SuppressRuntimeChanges,
            legacy ? ApiEquivalenceTable.Load().Enabled(config.SuppressApiEquivalences) : [],
            runtime,
            sites);

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
                Reasons(project),
                [.. own.Concat(counterpartList).Distinct()]));
        }

        return ([.. unmatched.Where(identity => !taken.Contains(identity))], projects.MoveToImmutable());
    }

    /// <summary>
    /// P2-018 (ADR 0029): after symbol enumeration, a loaded C# project that holds a type declaration but yields
    /// zero procedures is treated as a project the loader skipped, with reason
    /// <see cref="LoadDiagnosticKind.NoProcedures"/> ("a loaded project with source files that yields no procedures
    /// is a load failure, not an empty project"). A project with no type declaration at all (only assembly
    /// attributes, say) is unaffected: it was never going to yield procedures. So is one whose types declare no
    /// member with a body (P2-084: a placeholder class, fields, an enum): no method was there to lose.
    /// </summary>
    private static LoadedSolution SkipVacuousProjects(LoadedSolution loaded)
    {
        ImmutableArray<Compilation>.Builder kept = ImmutableArray.CreateBuilder<Compilation>();
        ImmutableArray<SkippedProject>.Builder vacuous = ImmutableArray.CreateBuilder<SkippedProject>();
        foreach (Compilation compilation in loaded.Compilations)
        {
            if (!ProcedureEnumerator.Enumerate(compilation).IsEmpty || !DeclaresABody(compilation))
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

    /// <summary>
    /// Whether any class, struct, interface, record or enum in the compilation's syntax trees declares a member with
    /// a body, an expression body or an accessor (P2-084).
    /// </summary>
    private static bool DeclaresABody(Compilation compilation) =>
        compilation.SyntaxTrees.Any(static tree => tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .Any(static type => type.DescendantNodes().Any(IsBody)));

    private static bool IsBody(SyntaxNode node) =>
        node is AccessorDeclarationSyntax or ArrowExpressionClauseSyntax or BaseMethodDeclarationSyntax { Body: not null };

    /// <summary>The reasons a project was skipped, as an <see cref="UnverifiedProject"/> and the debug log word them.</summary>
    private static ImmutableArray<string> Reasons(SkippedProject project) =>
        [.. project.Diagnostics.Select(static d => d.Id.Length > 0 ? $"{d.Id}: {d.Message}" : d.Message)];

    /// <summary>
    /// One side's <paramref name="phase"/> (ADR 0038; ticket M4-013): the side loaded, with an item per project it opened,
    /// weighted by document count, then per project it skipped, each of which is also a debug <see cref="IRunLog.Detail"/>.
    /// The events follow the load, since the project count is only known once it is done; a load that fails is one failed item.
    /// The loader is told which side it loads: a modern project that does not compile is kept, and its methods that do not
    /// bind are Unknown(Unbound) one by one (ADR 0029 as clarified by ticket P2-085).
    /// </summary>
    private LoadedSolution Loaded(string phase, string path, Codebase codebase, IRunLog log, CancellationToken ct)
    {
        LoadedSolution loaded;
        try
        {
            loaded = LoadOrThrow(path, codebase, ct);
        }
        catch (FrontendLoadException)
        {
            log.Phase(phase, 1, 1);
            log.Item(path, 1);
            log.ItemDone("failed");
            log.PhaseDone();
            throw;
        }

        loaded = SkipVacuousProjects(loaded);
        string side = phase["load-".Length..];
        log.Phase(phase, loaded.Compilations.Length + loaded.Skipped.Length, loaded.Compilations.Sum(Documents) + loaded.Skipped.Sum(SkippedDocuments));
        foreach (Compilation compilation in loaded.Compilations)
        {
            log.Item(compilation.AssemblyName!, Documents(compilation));
            log.ItemDone("loaded");
        }

        foreach (SkippedProject project in loaded.Skipped)
        {
            log.Item(project.Name, SkippedDocuments(project));
            log.ItemDone("skipped");
            if (log.IsDebug)
            {
                log.Detail($"{side} project {project.Name} skipped: {string.Join("; ", Reasons(project))}");
            }
        }

        log.PhaseDone();
        return loaded;
    }

    /// <summary>A project's weight in the load phases: its documents, at least 1 so an empty project still counts.</summary>
    private static int Documents(Compilation compilation) => Math.Max(1, compilation.SyntaxTrees.Count());

    private static int SkippedDocuments(SkippedProject project) => project.Compilation is { } compilation ? Documents(compilation) : 1;

    /// <summary>One side's item of the <c>enumerate</c> phase: <see cref="Procedures"/> over its compilations.</summary>
    private static (ImmutableArray<SideProcedure> Procedures, ImmutableArray<ProcedureIdentity> ForcedAmbiguous) Enumerated(
        string side, LoadedSolution loaded, EquivConfig config, EndpointOverrides overrides, IRunLog log)
    {
        log.Item(side, loaded.Compilations.Length);
        (ImmutableArray<SideProcedure> Procedures, ImmutableArray<ProcedureIdentity> ForcedAmbiguous) result = Procedures(loaded.Compilations, config.Renames, overrides);
        log.ItemDone($"{result.Procedures.Length} procedures");
        return result;
    }

    private LoadedSolution LoadOrThrow(string path, Codebase side, CancellationToken ct)
    {
        try
        {
            return _loader.LoadAsync(path, side, ct).GetAwaiter().GetResult();
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

        IEnumerable<(Compilation Compilation, EnumeratedProcedure Procedure)> enumerated = LastFlavour(
            compilations.SelectMany(static compilation => ProcedureEnumerator.Enumerate(compilation).Select(procedure => (compilation, procedure))),
            static procedure => procedure.Identity);
        foreach ((Compilation compilation, EnumeratedProcedure procedure) in enumerated)
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

        return (procedures.ToImmutable(), forcedAmbiguous.ToImmutable());
    }

    /// <summary>Every endpoint a side declares, each target-framework flavour of a project counted once (<see cref="LastFlavour"/>).</summary>
    private static ImmutableArray<Endpoint> Endpoints(ImmutableArray<Compilation> compilations) =>
    [
        .. LastFlavour(
            compilations.SelectMany(static compilation => EndpointDiscovery.Discover(compilation).Select(endpoint => (compilation, endpoint))),
            static endpoint => endpoint).Select(static item => item.Item),
    ];

    /// <summary>
    /// P2-016: a multi-targeted project loads once per target framework, every flavour under the same assembly name, so
    /// without this each of its declarations would be on its side several times and never match (Ambiguous is for
    /// overloads, VERIFICATION-MODEL.md section 4). Of the <paramref name="items"/> that share an assembly name and a
    /// <paramref name="key"/>, only those from the last compilation holding that key are kept: flavours load in
    /// <c>TargetFrameworks</c> order, which by convention ends at the newest framework, the one nearest a migration's
    /// target. Duplicates within one compilation, and across assemblies (a linked file), are kept as they are.
    /// </summary>
    private static IEnumerable<(Compilation Compilation, T Item)> LastFlavour<T, TKey>(
        IEnumerable<(Compilation Compilation, T Item)> items, Func<T, TKey> key) =>
        items
            .GroupBy(item => (item.Compilation.AssemblyName, Key: key(item.Item)))
            .SelectMany(static group =>
            {
                Compilation last = group.Last().Compilation;
                return group.Where(item => ReferenceEquals(item.Compilation, last));
            });

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

    /// <summary>Each side's project runtimes by assembly name, as <see cref="RuntimeDetection"/> keys them: one entry per loaded project.</summary>
    private static Dictionary<string, ProjectRuntime> ByProject(ImmutableArray<ProjectRuntime> runtimes) =>
        runtimes.ToDictionary(static r => r.Project, StringComparer.Ordinal);

    private sealed record SideProcedure(ProcedureIdentity Identity, IMethodSymbol Symbol, Compilation Compilation);

    /// <summary>Both sides' procedures by identity and project runtimes by assembly name, which lowering a pair reads.</summary>
    private sealed record Sides(
        Dictionary<ProcedureIdentity, SideProcedure> Legacy,
        Dictionary<string, ProjectRuntime> LegacyRuntimes,
        Dictionary<ProcedureIdentity, SideProcedure> Modern,
        Dictionary<string, ProjectRuntime> ModernRuntimes);
}
