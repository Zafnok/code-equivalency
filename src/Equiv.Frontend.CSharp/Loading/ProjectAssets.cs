using System.Collections.Immutable;
using System.Text.Json;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// A non-SDK project's PackageReference compile assets, as NuGet's own restore resolved them into
/// <c>project.assets.json</c> (M3-029). This is what Visual Studio's <c>ResolveNuGetPackageAssets</c> reads; the bare
/// loader does not resolve the package graph itself. Each package's <c>compile</c> assets become file references and
/// its <c>frameworkAssemblies</c> (from the nuspec) become framework references.
/// </summary>
internal sealed record ProjectAssets(ImmutableArray<string> CompileAssets, ImmutableArray<string> FrameworkAssemblies)
{
    /// <exception cref="KeyNotFoundException">The file has no target for <paramref name="targetFramework"/>.</exception>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    public static ProjectAssets Read(string json, string targetFramework)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        ImmutableArray<string> folders = [.. root.GetProperty("packageFolders").EnumerateObject().Select(static f => f.Name)];
        JsonElement libraries = root.GetProperty("libraries");
        JsonElement target = root.GetProperty("targets").EnumerateObject()
            .FirstOrDefault(t => string.Equals(t.Name, targetFramework, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.Object } found
                ? found.Value
                : throw new KeyNotFoundException($"project.assets.json has no target for '{targetFramework}'");

        ImmutableArray<string>.Builder assets = ImmutableArray.CreateBuilder<string>();
        ImmutableArray<string>.Builder frameworkAssemblies = ImmutableArray.CreateBuilder<string>();
        foreach (JsonProperty package in target.EnumerateObject().Where(static p => p.Value.TryGetProperty("type", out JsonElement type) && string.Equals(type.GetString(), "package", StringComparison.Ordinal)))
        {
            string path = libraries.GetProperty(package.Name).GetProperty("path").GetString()!;
            if (package.Value.TryGetProperty("compile", out JsonElement compile))
            {
                assets.AddRange(compile.EnumerateObject()
                    .Where(static a => !a.Name.EndsWith("_._", StringComparison.Ordinal))
                    .Select(a => Locate(folders, path, a.Name)));
            }

            if (package.Value.TryGetProperty("frameworkAssemblies", out JsonElement framework))
            {
                frameworkAssemblies.AddRange(framework.EnumerateArray().Select(static f => f.GetString()!));
            }
        }

        return new ProjectAssets(assets.ToImmutable(), frameworkAssemblies.ToImmutable());
    }

    /// <summary>The asset in the first package folder that has it, as NuGet looks them up; else its path in the first folder.</summary>
    private static string Locate(ImmutableArray<string> folders, string packagePath, string asset)
    {
        ImmutableArray<string> candidates = [.. folders.Select(f => ProjectPath.Full(f, Path.Combine(packagePath, asset)))];
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
