using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// A non-SDK project's metadata references, resolved the way MSBuild's <c>ResolveAssemblyReference</c> does for the
/// Windows loader (M3-029). A <c>Reference</c> item resolves by its HintPath first, then from the framework directory
/// and its <c>Facades</c>, then as a file name; one that resolves nowhere is left out, as MSBuild leaves it out.
/// Implicit references, as measured against MSBuild: <c>mscorlib</c> always (unless <c>NoStdLib</c>) and
/// <c>System.Core</c> from .NET Framework 3.5. Facades: a reference that depends on <c>System.Runtime</c> pulls in
/// <c>Facades/*.dll</c>; one that depends on <c>netstandard</c> pulls in <c>Facades/netstandard.dll</c> from 4.7.1,
/// and before that, from 4.6.1, the shims in the .NET SDK's <c>Microsoft.NET.Build.Extensions</c> folder. The first
/// reference with a given file name wins.
/// </summary>
internal static class AssemblyReferences
{
    private static readonly Version DotNet35 = new(3, 5);
    private static readonly Version DotNet461 = new(4, 6, 1);
    private static readonly Version DotNet471 = new(4, 7, 1);

    /// <param name="netStandardShims">The SDK's <c>Microsoft.NET.Build.Extensions/net461/lib</c> folder, if there is one.</param>
    public static ImmutableArray<ResolvedReference> Resolve(
        EvaluatedProject project,
        string frameworkDirectory,
        Version frameworkVersion,
        ProjectAssets assets,
        string? netStandardShims)
    {
        ImmutableArray<ResolvedReference> direct = Distinct(Direct(project, frameworkDirectory, frameworkVersion, assets));
        ImmutableArray<string> roots = [.. direct.Select(static r => r.Path)];
        return Distinct([.. direct, .. Facades(project.Properties, frameworkDirectory, frameworkVersion, roots, netStandardShims).Select(ResolvedReference.Plain)]);
    }

    /// <summary>The first reference with a given file name wins, as the Windows loader keeps one per assembly.</summary>
    private static ImmutableArray<ResolvedReference> Distinct(IEnumerable<ResolvedReference> references) =>
        [.. references.DistinctBy(static r => Path.GetFileName(r.Path), StringComparer.OrdinalIgnoreCase)];

    private static IEnumerable<ResolvedReference> Direct(EvaluatedProject project, string frameworkDirectory, Version frameworkVersion, ProjectAssets assets)
    {
        if (!project.Properties.IsTrue("NoStdLib") && ProjectPath.Resolve(frameworkDirectory, "mscorlib.dll") is { } mscorlib)
        {
            yield return ResolvedReference.Plain(mscorlib);
        }

        string facades = Path.Combine(frameworkDirectory, "Facades");
        foreach (EvaluatedItem reference in project.OfType("Reference"))
        {
            string name = reference.Include.Split(',')[0].Trim();
            string? path = (reference.Metadatum("HintPath") is { Length: > 0 } hint ? ProjectPath.Resolve(project.Directory, hint) : null)
                ?? ProjectPath.Resolve(frameworkDirectory, name + ".dll")
                ?? ProjectPath.Resolve(facades, name + ".dll")
                ?? ProjectPath.Resolve(project.Directory, name);
            if (path is not null && File.Exists(path))
            {
                yield return new ResolvedReference(
                    path,
                    [.. (reference.Metadatum("Aliases") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                    string.Equals(reference.Metadatum("EmbedInteropTypes"), "true", StringComparison.OrdinalIgnoreCase));
            }
        }

        foreach (string name in assets.FrameworkAssemblies)
        {
            if (ProjectPath.Resolve(frameworkDirectory, name + ".dll") is { } path)
            {
                yield return ResolvedReference.Plain(path);
            }
        }

        foreach (string asset in assets.CompileAssets.Where(File.Exists))
        {
            yield return ResolvedReference.Plain(asset);
        }

        if (frameworkVersion >= DotNet35
            && !IsFalse(project.Properties, "AddAdditionalExplicitAssemblyReferences")
            && ProjectPath.Resolve(frameworkDirectory, "System.Core.dll") is { } systemCore)
        {
            yield return ResolvedReference.Plain(systemCore);
        }
    }

    private static IEnumerable<string> Facades(MsBuildProperties properties, string frameworkDirectory, Version frameworkVersion, ImmutableArray<string> roots, string? netStandardShims)
    {
        string facades = Path.Combine(frameworkDirectory, "Facades");
        IEnumerable<string> designTime = !IsFalse(properties, "ImplicitlyExpandDesignTimeFacades") && Directory.Exists(facades) && DependsOn(roots, "System.Runtime", frameworkDirectory)
            ? Directory.EnumerateFiles(facades, "*.dll").Order(StringComparer.Ordinal)
            : [];
        IEnumerable<string> netStandard = IsFalse(properties, "ImplicitlyExpandNETStandardFacades") || frameworkVersion < DotNet461 || !DependsOn(roots, "netstandard", frameworkDirectory) ? []
            : frameworkVersion >= DotNet471 ? [.. ProjectPath.Resolve(facades, "netstandard.dll") is { } netstandardFacade ? [netstandardFacade] : Array.Empty<string>()]
            : netStandardShims is null ? []
            : Directory.EnumerateFiles(netStandardShims, "*.dll").Order(StringComparer.Ordinal);
        return designTime.Concat(netStandard);
    }

    private static bool IsFalse(MsBuildProperties properties, string name) => properties.Read(name).Trim().Equals("false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="assembly"/> is one of <paramref name="roots"/> or among their dependencies, followed
    /// through the files next to each reference, as <c>ResolveAssemblyReference</c> finds dependencies. Framework
    /// assemblies are not followed, as <c>ResolveAssemblyReference</c> does not follow them.
    /// </summary>
    private static bool DependsOn(ImmutableArray<string> roots, string assembly, string frameworkDirectory)
    {
        Queue<string> queue = new(roots);
        HashSet<string> seen = new(roots, StringComparer.OrdinalIgnoreCase);
        while (queue.TryDequeue(out string? path))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(path), assembly, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(Path.GetDirectoryName(path), frameworkDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string reference in ReferencedAssemblies(path))
            {
                if (string.Equals(reference, assembly, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (ProjectPath.Resolve(Path.GetDirectoryName(path)!, reference + ".dll") is { } dependency && seen.Add(dependency))
                {
                    queue.Enqueue(dependency);
                }
            }
        }

        return false;
    }

    private static ImmutableArray<string> ReferencedAssemblies(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            using PEReader reader = new(stream);
            if (!reader.HasMetadata)
            {
                return [];
            }

            MetadataReader metadata = reader.GetMetadataReader();
            return [.. metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name))];
        }
        catch (BadImageFormatException)
        {
            return [];
        }
    }
}
