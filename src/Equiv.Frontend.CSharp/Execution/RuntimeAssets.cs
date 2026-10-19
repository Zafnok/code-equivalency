using System.Collections.Immutable;
using System.Text.Json;

using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// What each NuGet package a side's projects compile against runs as on this machine, as the project's own build output
/// would deploy it (ticket P2-240). A package's compile asset can be a reference assembly whose implementation is elsewhere
/// in the package: <c>System.Management</c> 4.7.0 compiles against <c>ref/netstandard2.0</c> and runs
/// <c>runtimes/win/lib/netcoreapp2.0</c> on Windows. A project's <c>project.assets.json</c> names, for the target its
/// compilation was built for, each package's <c>runtime</c> assets and its <c>runtimeTargets</c>, which replace them on
/// the most specific runtime identifier this machine's falls back to (<see cref="Chain"/>), as the host picks them from a
/// <c>deps.json</c>. A driver has no <c>deps.json</c>, so the chosen files go beside it. A package that a referenced
/// project compiles against is resolved by the deployed project's restore first, which unified its version, as the
/// deployed project's build output is. A <c>packages.config</c> project has no restore file: its references are each
/// package's <c>lib/&lt;tfm&gt;</c> implementation already.
/// </summary>
internal sealed class RuntimeAssets(Lazy<IReadOnlyDictionary<string, string>> restoreFiles, IReadOnlyList<string> rids)
{
    private const string AssetsFileName = "project.assets.json";

    /// <summary>The obj folder sits at most this many levels above a project's intermediate assembly (<c>obj/Debug/net10.0/win-x64</c>).</summary>
    private const int IntermediateDepth = 4;

    /// <summary>No project has a restore: every reference is deployed as it was before ticket P2-240.</summary>
    public static readonly RuntimeAssets None = new(ImmutableDictionary<string, string>.Empty, ["any"]);

    private readonly Dictionary<string, Restore?> restores = new(StringComparer.Ordinal);

    public RuntimeAssets(IReadOnlyDictionary<string, string> restoreFiles, IReadOnlyList<string> rids)
        : this(new Lazy<IReadOnlyDictionary<string, string>>(restoreFiles), rids)
    {
    }

    /// <summary>Each project's <c>project.assets.json</c>, by assembly name.</summary>
    internal IReadOnlyDictionary<string, string> RestoreFiles => restoreFiles.Value;

    /// <summary>
    /// The restore file of each project in <paramref name="solution"/>, by assembly name, resolved for <paramref name="rid"/>,
    /// looked for when a replay first needs one. The file is in the project's <c>MSBuildProjectExtensionsPath</c>, which its
    /// intermediate output path sits under, else in the default <c>obj</c> beside the project file.
    /// </summary>
    public static RuntimeAssets Of(Solution solution, string rid) => new(new Lazy<IReadOnlyDictionary<string, string>>(() => Files(solution)), Chain(rid));

    private static Dictionary<string, string> Files(Solution solution)
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        foreach (Project project in solution.Projects)
        {
            string? obj = Path.GetDirectoryName(project.FilePath) is { } directory ? Path.Combine(directory, "obj", AssetsFileName) : null;
            if (Above(project.CompilationOutputInfo.AssemblyPath).Append(obj).OfType<string>().FirstOrDefault(File.Exists) is { } file)
            {
                files.TryAdd(project.AssemblyName, file);
            }
        }

        return files;
    }

    /// <summary>
    /// The runtime identifiers <paramref name="rid"/> falls back to, most specific first, in the portable graph the .NET 8
    /// host uses: <c>win-x64</c>, <c>win</c>, <c>any</c>; <c>linux-x64</c>, <c>linux</c>, <c>unix-x64</c>, <c>unix</c>, <c>any</c>.
    /// </summary>
    public static IReadOnlyList<string> Chain(string rid)
    {
        ArgumentNullException.ThrowIfNull(rid);

        int dash = rid.LastIndexOf('-');
        if (dash <= 0)
        {
            return [.. new[] { rid, "any" }.Distinct(StringComparer.Ordinal)];
        }

        string os = rid[..dash];
        return os.StartsWith("win", StringComparison.Ordinal) ? [rid, os, "any"] : [rid, os, "unix-" + rid[(dash + 1)..], "unix", "any"];
    }

    /// <summary>
    /// The files that run in place of <paramref name="path"/>, which <paramref name="compilation"/> references, in the folder
    /// <paramref name="deployed"/> is deployed to; or why it cannot be deployed. Null when no restore names the file as a
    /// package's compile asset, and it is not a package's reference assembly (<paramref name="referenceAssembly"/>) either:
    /// such a file runs as it is, or the runtime supplies it.
    /// </summary>
    public PackageFiles? For(Compilation deployed, Compilation compilation, string path, bool referenceAssembly)
    {
        ArgumentNullException.ThrowIfNull(deployed);
        ArgumentNullException.ThrowIfNull(compilation);

        string full = Path.GetFullPath(path);
        Restore? top = Restored(deployed);
        Restore? own = Restored(compilation);
        if ((top?.Compiling(full) ?? own?.Compiling(full)) is not { } compiled)
        {
            return referenceAssembly && Unrestored(full) is { } name
                ? new PackageFiles([], $"the package {name} is compiled against its reference assembly, and no restore names its runtime asset")
                : null;
        }

        Package package = top?.Named(compiled.Id) ?? compiled;
        return package.Missing is { } missing
            ? new PackageFiles([], $"the package {package.Id} {package.Version} runs {missing}, which is not on disk")
            : new PackageFiles(package.Runtime, Error: null);
    }

    /// <summary>
    /// <paramref name="compilation"/>'s restore, read once. A project with no restore file caches nothing, so
    /// <see cref="None"/>, which every caller shares, is never written to.
    /// </summary>
    private Restore? Restored(Compilation compilation)
    {
        string name = compilation.AssemblyName!;
        if (!restoreFiles.Value.TryGetValue(name, out string? file))
        {
            return null;
        }

        if (!restores.TryGetValue(name, out Restore? restore))
        {
            restore = File.Exists(file) ? Restore.Read(File.ReadAllText(file), References(compilation), rids) : null;
            restores[name] = restore;
        }

        return restore;
    }

    private static HashSet<string> References(Compilation compilation) =>
        new(compilation.References.OfType<PortableExecutableReference>().Select(static r => r.FilePath).OfType<string>().Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

    /// <summary>The restore file in each of the <see cref="IntermediateDepth"/> folders above <paramref name="assembly"/>.</summary>
    private static IEnumerable<string> Above(string? assembly)
    {
        string? directory = Path.GetDirectoryName(assembly);
        for (int level = 0; level < IntermediateDepth && directory is not null; level++)
        {
            yield return Path.Combine(directory, AssetsFileName);
            directory = Path.GetDirectoryName(directory);
        }
    }

    /// <summary>
    /// <c>&lt;id&gt; &lt;version&gt;</c> when <paramref name="path"/> is in a restored package's <c>ref/&lt;tfm&gt;</c>
    /// folder (<c>&lt;root&gt;/&lt;id&gt;/&lt;version&gt;</c>, with its <c>.nuspec</c>), else null. A targeting pack
    /// restored as a package (<c>Microsoft.NETCore.App.Ref</c>) has the same shape and lists its assemblies in
    /// <c>data/FrameworkList.xml</c>: the runtime supplies those, so it is not one.
    /// </summary>
    private static string? Unrestored(string path)
    {
        DirectoryInfo framework = new FileInfo(path).Directory!;
        return framework.Parent is { Name: var folder, Parent: { Parent: { } id } package }
            && string.Equals(folder, "ref", StringComparison.OrdinalIgnoreCase)
            && package.EnumerateFiles("*.nuspec").Any()
            && !File.Exists(Path.Combine(package.FullName, "data", "FrameworkList.xml"))
                ? $"{id.Name} {package.Name}"
                : null;
    }

    /// <summary>One package at the target a project was built for: its compile assets and the runtime assets chosen for this machine.</summary>
    private sealed record Package(string Id, string Version, ImmutableArray<string> Compile, ImmutableArray<string> Runtime)
    {
        /// <summary>The first runtime asset that is not on disk, or null.</summary>
        public string? Missing { get; } = Runtime.FirstOrDefault(static f => !File.Exists(f));
    }

    /// <summary>One project's restore, at the target its compilation was built for.</summary>
    private sealed class Restore(ImmutableArray<Package> packages)
    {
        public Package? Compiling(string path) => packages.FirstOrDefault(p => p.Compile.Contains(path, StringComparer.OrdinalIgnoreCase));

        public Package? Named(string id) => packages.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The restore <paramref name="json"/> holds, at its target without a runtime identifier whose compile assets
        /// <paramref name="references"/> holds the most of: the one the compilation was built for, even when the project
        /// targets several frameworks. Null when the file cannot be read: no package of it is then deployed from it.
        /// </summary>
        public static Restore? Read(string json, HashSet<string> references, IReadOnlyList<string> rids)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                ImmutableArray<string> folders = [.. root.GetProperty("packageFolders").EnumerateObject().Select(static f => f.Name)];
                JsonElement libraries = root.GetProperty("libraries");
                return new Restore(root.GetProperty("targets").EnumerateObject()
                    .Where(static t => !t.Name.Contains('/', StringComparison.Ordinal))
                    .Select(t => Packages(t.Value, libraries, folders, rids))
                    .DefaultIfEmpty([])
                    .MaxBy(p => p.Sum(package => package.Compile.Count(references.Contains))));
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return null;
            }
        }

        private static ImmutableArray<Package> Packages(JsonElement target, JsonElement libraries, ImmutableArray<string> folders, IReadOnlyList<string> rids) =>
        [
            .. target.EnumerateObject()
                .Where(static p => p.Value.TryGetProperty("type", out JsonElement type) && string.Equals(type.GetString(), "package", StringComparison.Ordinal))
                .Select(p =>
                {
                    string library = libraries.GetProperty(p.Name).GetProperty("path").GetString()!;
                    int slash = p.Name.IndexOf('/', StringComparison.Ordinal);
                    return new Package(
                        p.Name[..slash],
                        p.Name[(slash + 1)..],
                        [.. Assets(p.Value, "compile").Select(a => Locate(folders, library, a))],
                        [.. Runtime(p.Value, rids).Select(a => Locate(folders, library, a))]);
                }),
        ];

        /// <summary>
        /// The runtime assets the host would load: those for the most specific runtime identifier in <paramref name="rids"/>
        /// that the package's <c>runtimeTargets</c> name, else its plain <c>runtime</c> assets.
        /// </summary>
        private static IEnumerable<string> Runtime(JsonElement package, IReadOnlyList<string> rids)
        {
            List<(string Asset, string Rid)> specific = package.TryGetProperty("runtimeTargets", out JsonElement targets)
                ? [.. targets.EnumerateObject()
                    .Where(static a => string.Equals(a.Value.GetProperty("assetType").GetString(), "runtime", StringComparison.Ordinal))
                    .Select(static a => (a.Name, a.Value.GetProperty("rid").GetString()!))]
                : [];
            return rids.FirstOrDefault(r => specific.Exists(s => string.Equals(s.Rid, r, StringComparison.Ordinal))) is { } best
                ? specific.Where(s => string.Equals(s.Rid, best, StringComparison.Ordinal)).Select(static s => s.Asset).Where(static a => !Placeholder(a))
                : Assets(package, "runtime");
        }

        /// <summary>The assets of one kind, without NuGet's <c>_._</c> placeholder for an empty folder.</summary>
        private static IEnumerable<string> Assets(JsonElement package, string kind) =>
            package.TryGetProperty(kind, out JsonElement assets) ? assets.EnumerateObject().Select(static a => a.Name).Where(static a => !Placeholder(a)) : [];

        private static bool Placeholder(string asset) => asset.EndsWith("_._", StringComparison.Ordinal);

        /// <summary>The asset in the first package folder that has it, as NuGet looks them up; else its path in the first folder.</summary>
        private static string Locate(ImmutableArray<string> folders, string library, string asset)
        {
            ImmutableArray<string> candidates = [.. folders.Select(f => ProjectPath.Full(f, Path.Combine(library, asset)))];
            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }
    }
}
