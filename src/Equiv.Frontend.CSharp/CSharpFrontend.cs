using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// <see cref="ILanguageFrontend"/> for C# (ARCHITECTURE.md): loads both sides through
/// <see cref="ISolutionLoader"/> (M2-001), enumerates procedures (M2-002), applies the config's
/// rename map, and hands the identity sets to <see cref="IProcedureMatcher"/>.
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

        ImmutableArray<ProcedureIdentity> legacyIdentities = Identities(legacy, config.Renames);
        ImmutableArray<ProcedureIdentity> modernIdentities = Identities(modern, config.Renames);

        return _matcher.Match(legacyIdentities, modernIdentities);
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
    /// Rename-applied identities for a loaded side (acceptance criterion 3's "apply the config
    /// rename map"), each carrying the declaring file/line (acceptance criterion 5) converted from
    /// <see cref="EnumeratedProcedure.Location"/>.
    /// </summary>
    private static ImmutableArray<ProcedureIdentity> Identities(LoadedSolution solution, RenameMap renames) =>
        [.. solution.Compilations
            .SelectMany(static compilation => ProcedureEnumerator.Enumerate(compilation))
            .Select(procedure => RoslynIdentity.Of(procedure.Symbol, renames) with { Location = ToSourceSpan(procedure.Location) })];

    private static SourceSpan ToSourceSpan(Location location)
    {
        FileLinePositionSpan span = location.GetLineSpan();
        return new SourceSpan(
            span.Path,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            span.EndLinePosition.Character + 1);
    }
}
