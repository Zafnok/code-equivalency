using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// Downloads <c>.nupkg</c> files from a solution's package sources (M3-029), with no nuget.exe, Mono or MSBuild: an
/// HTTP source through its NuGet v3 service index and the <c>PackageBaseAddress/3.0.0</c> flat container, a local
/// folder as either a flat folder or a v3 folder layout. Sources are tried in order; the first that has the package
/// wins. Every HTTP request goes through <c>get</c>, the seam behind which only the network factory
/// (<see cref="HttpPackageSource"/>) is untested; it returns null for a missing resource.
/// </summary>
internal sealed class PackageFeed(ImmutableArray<string> sources, Func<Uri, CancellationToken, Task<byte[]?>> get)
{
    private readonly Dictionary<string, string?> _baseAddresses = new(StringComparer.OrdinalIgnoreCase);

    public ImmutableArray<string> Sources => sources;

    /// <summary>The package's bytes from the first source that has it, or null.</summary>
    public async Task<byte[]?> DownloadAsync(string id, string version, CancellationToken ct)
    {
        string lowerId = id.ToLowerInvariant();
        string lowerVersion = NormalizeVersion(version).ToLowerInvariant();
        foreach (string source in sources)
        {
            byte[]? package = NuGetSettings.IsRemote(source)
                ? await FromServiceAsync(source, lowerId, lowerVersion, ct).ConfigureAwait(false)
                : await FromFolderAsync(source, $"{id}.{version}.nupkg", $"{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg", ct).ConfigureAwait(false);
            if (package is not null)
            {
                return package;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts a package into <paramref name="directory"/> the way nuget.exe lays out a <c>packages.config</c> folder:
    /// the package's own files plus the <c>.nupkg</c> itself as <paramref name="packageFileName"/>, without the OPC
    /// parts. An entry that would land outside the directory is ignored.
    /// </summary>
    public static void Extract(byte[] package, string directory, string packageFileName)
    {
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, packageFileName), package);
        using ZipArchive zip = new(new MemoryStream(package), ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = Uri.UnescapeDataString(entry.FullName);
            string target = Path.GetFullPath(Path.Combine(root, name));
            if (name.EndsWith('/') || name.StartsWith("_rels/", StringComparison.Ordinal) || name.StartsWith("package/", StringComparison.Ordinal)
                || string.Equals(name, "[Content_Types].xml", StringComparison.Ordinal) || !target.StartsWith(root, StringComparison.Ordinal))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>NuGet's normalized version: at least three parts, a zero fourth part dropped, leading zeros and build metadata removed.</summary>
    public static string NormalizeVersion(string version)
    {
        string core = version.Split('+')[0].Trim();
        int dash = core.IndexOf('-', StringComparison.Ordinal);
        List<string> parts = [.. (dash < 0 ? core : core[..dash]).Split('.').Select(static p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n.ToString(CultureInfo.InvariantCulture) : p)];
        while (parts.Count < 3)
        {
            parts.Add("0");
        }

        if (parts.Count == 4 && string.Equals(parts[3], "0", StringComparison.Ordinal))
        {
            parts.RemoveAt(3);
        }

        return string.Join('.', parts) + (dash < 0 ? string.Empty : core[dash..]);
    }

    private async Task<byte[]?> FromServiceAsync(string source, string lowerId, string lowerVersion, CancellationToken ct)
    {
        try
        {
            return await BaseAddressAsync(source, ct).ConfigureAwait(false) is { } baseAddress
                ? await get(new Uri($"{baseAddress}{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg"), ct).ConfigureAwait(false)
                : null;
        }
        catch (HttpRequestException)
        {
            // An unreachable source is one that does not have the package; the next one is tried.
            return null;
        }
    }

    /// <summary>The flat-container address a v3 service index names, or null for a source that has none (a v2 feed).</summary>
    private async Task<string?> BaseAddressAsync(string source, CancellationToken ct)
    {
        if (!_baseAddresses.TryGetValue(source, out string? baseAddress))
        {
            byte[]? index = await get(new Uri(source), ct).ConfigureAwait(false);
            baseAddress = index is null ? null : PackageBaseAddress(index);
            _baseAddresses[source] = baseAddress;
        }

        return baseAddress;
    }

    private static string? PackageBaseAddress(byte[] index)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(index);
            return json.RootElement.TryGetProperty("resources", out JsonElement resources) && resources.ValueKind == JsonValueKind.Array
                ? resources.EnumerateArray()
                    .Where(static r => r.TryGetProperty("@type", out JsonElement type) && type.GetString() is { } t && t.StartsWith("PackageBaseAddress/3.0.0", StringComparison.Ordinal))
                    .Select(static r => r.GetProperty("@id").GetString()!.TrimEnd('/') + "/")
                    .FirstOrDefault()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> FromFolderAsync(string folder, string flatName, string v3Path, CancellationToken ct)
    {
        string? file = ProjectPath.Resolve(folder, flatName) ?? ProjectPath.Resolve(folder, v3Path);
        return file is null ? null : await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
    }
}
