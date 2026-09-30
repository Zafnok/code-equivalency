using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// Each loaded project's runtime (ADR 0040 decision 1; ticket P2-053), read from its compilation's
/// <c>System.Runtime.Versioning.TargetFrameworkAttribute</c>. Both loaders' compilations carry it: MSBuild generates it
/// for an SDK-style project, and the bare loader writes the same attribute (<see cref="BareProjectLoader"/>). A
/// multi-targeted project is its last flavour, the one <c>CSharpFrontend.LastFlavour</c> analyses (P2-016).
/// A project that names no .NET Framework or .NET (Core) runtime, a <c>netstandard</c> library, runs on its hosts: the
/// executable and test projects on the same side that reference it, directly or transitively. With none, it takes the
/// side's configured runtime (<c>equiv.json</c>'s <c>runtimes</c>), else it is unhosted and keeps its own target framework.
/// </summary>
internal static class RuntimeDetection
{
    public const string Attribute = "attribute";
    public const string Host = "host";
    public const string Config = "config";
    public const string Unhosted = "unhosted";

    /// <summary>What a project with no <c>TargetFrameworkAttribute</c> reports when it is unhosted.</summary>
    public const string Unknown = "unknown";

    private const string TargetFrameworkAttribute = "System.Runtime.Versioning.TargetFrameworkAttribute";
    private const string StandardIdentifier = ".NETStandard";

    /// <summary>Test framework assemblies whose reference makes a project a test project, and so a host.</summary>
    private static readonly FrozenSet<string> TestFrameworks = new[]
    {
        "xunit.core",
        "xunit.v3.core",
        "nunit.framework",
        "Microsoft.VisualStudio.TestPlatform.TestFramework",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>One entry per loaded project of a side, ordered by project name; <paramref name="configured"/> is that side's <c>runtimes</c> value.</summary>
    public static ImmutableArray<ProjectRuntime> Detect(ImmutableArray<Compilation> compilations, TargetRuntime? configured)
    {
        // A C# project's compilation always has an assembly name; later flavours replace earlier ones (P2-016).
        Dictionary<string, Compilation> projects = new(StringComparer.Ordinal);
        foreach (Compilation compilation in compilations)
        {
            projects[compilation.AssemblyName!] = compilation;
        }

        Dictionary<string, string?> declared = projects.ToDictionary(static p => p.Key, static p => Declared(p.Value), StringComparer.Ordinal);
        Dictionary<string, TargetRuntime?> own = declared.ToDictionary(static p => p.Key, static p => p.Value is { } moniker ? TargetRuntime.Parse(moniker) : null, StringComparer.Ordinal);
        Dictionary<string, ImmutableArray<string>> references = projects.ToDictionary(
            static p => p.Key,
            p => ImmutableArray.CreateRange(p.Value.ReferencedAssemblyNames.Select(static identity => identity.Name).Where(projects.ContainsKey)),
            StringComparer.Ordinal);
        ImmutableArray<(TargetRuntime Runtime, HashSet<string> Reaches)> hosts =
        [
            .. projects
                .Where(p => own[p.Key] is not null && IsHost(p.Value))
                .Select(p => (own[p.Key]!, Reachable(p.Key, references))),
        ];

        return
        [
            .. projects.Keys.Order(StringComparer.Ordinal).Select(name => own[name] is { } runtime
                ? new ProjectRuntime(name, [runtime], runtime.ToString(), Attribute)
                : Resolved(name, declared[name], hosts, configured)),
        ];
    }

    private static ProjectRuntime Resolved(string name, string? declared, ImmutableArray<(TargetRuntime Runtime, HashSet<string> Reaches)> hosts, TargetRuntime? configured)
    {
        ImmutableArray<TargetRuntime> hosted = [.. hosts.Where(h => h.Reaches.Contains(name)).Select(static h => h.Runtime).Distinct().Order()];
        string own = ShortName(declared);
        return (hosted.IsEmpty, configured) switch
        {
            (false, _) => new ProjectRuntime(name, hosted, own, Host),
            (true, { } runtime) => new ProjectRuntime(name, [runtime], own, Config),
            _ => new ProjectRuntime(name, [], own, Unhosted),
        };
    }

    /// <summary>The compilation's <c>TargetFrameworkAttribute</c> moniker, read through Roslyn's attribute API, or null without one.</summary>
    internal static string? Declared(Compilation compilation) =>
        compilation.Assembly.GetAttributes()
            .Where(static a => string.Equals(a.AttributeClass!.ToDisplayString(), TargetFrameworkAttribute, StringComparison.Ordinal))
            .Select(static a => a.ConstructorArguments is [{ Value: string moniker }] ? moniker : null)
            .FirstOrDefault();

    /// <summary>An executable, or a project that references a test framework.</summary>
    private static bool IsHost(Compilation compilation) =>
        compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication or OutputKind.WindowsRuntimeApplication
        || compilation.ReferencedAssemblyNames.Any(static identity => TestFrameworks.Contains(identity.Name));

    /// <summary>Every loaded project <paramref name="host"/> references, directly or transitively.</summary>
    private static HashSet<string> Reachable(string host, Dictionary<string, ImmutableArray<string>> references)
    {
        HashSet<string> reached = new(StringComparer.Ordinal);
        Stack<string> pending = new([host]);
        while (pending.TryPop(out string? current))
        {
            foreach (string referenced in references[current].Where(reached.Add))
            {
                pending.Push(referenced);
            }
        }

        return reached;
    }

    /// <summary><c>.NETStandard,Version=v2.0</c> as <c>netstandard2.0</c>; any other moniker as written; no moniker as <see cref="Unknown"/>.</summary>
    private static string ShortName(string? moniker)
    {
        if (moniker is null)
        {
            return Unknown;
        }

        string[] parts = moniker.Split(',', StringSplitOptions.TrimEntries);
        string? version = parts.Skip(1).FirstOrDefault(static p => p.StartsWith("Version=v", StringComparison.OrdinalIgnoreCase))?["Version=v".Length..];
        return string.Equals(parts[0], StandardIdentifier, StringComparison.OrdinalIgnoreCase) && version is not null
            ? "netstandard" + version
            : moniker;
    }
}
