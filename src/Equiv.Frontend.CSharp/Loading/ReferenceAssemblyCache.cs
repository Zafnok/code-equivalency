namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// .NET Framework reference assemblies for the bare loader (M3-029), in the <c>&lt;root&gt;/.NETFramework/v&lt;x&gt;/</c>
/// layout of <c>Microsoft.NETFramework.ReferenceAssemblies.&lt;tfm&gt;</c>. A framework version is fetched from the
/// package sources on first use and kept; a version the cache already holds is never fetched again. The root is
/// <c>$EQUIV_REFERENCE_ASSEMBLIES</c>, else <c>equiv/reference-assemblies</c> under the local application data folder.
/// </summary>
internal sealed class ReferenceAssemblyCache(string root)
{
    public const string RootVariable = "EQUIV_REFERENCE_ASSEMBLIES";

    /// <summary>The package version every <c>Microsoft.NETFramework.ReferenceAssemblies.*</c> package is taken at, as in <c>tools/corpus</c>.</summary>
    public const string PackageVersion = "1.0.3";

    public string Root => root;

    public static string DefaultRoot(Func<string, string?> environment) =>
        environment(RootVariable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify) is { Length: > 0 } local ? local : Path.GetTempPath(),
                "equiv",
                "reference-assemblies");

    /// <summary>The directory holding the reference assemblies for <paramref name="frameworkVersion"/> (<c>v4.8</c>), or null when no source has them.</summary>
    public async Task<string?> DirectoryAsync(string frameworkVersion, PackageFeed feed, CancellationToken ct)
    {
        string directory = Path.Combine(root, ".NETFramework", frameworkVersion);
        if (Directory.Exists(directory))
        {
            return directory;
        }

        string moniker = "net" + frameworkVersion.TrimStart('v', 'V').Replace(".", string.Empty, StringComparison.Ordinal);
        string id = $"Microsoft.NETFramework.ReferenceAssemblies.{moniker}";
        if (await feed.DownloadAsync(id, PackageVersion, ct).ConfigureAwait(false) is not { } package)
        {
            return null;
        }

        // Extracted beside the cache and moved into place whole, so a failed fetch never leaves half a framework.
        string scratch = Path.Combine(root, $".fetch-{Guid.NewGuid():N}");
        try
        {
            PackageFeed.Extract(package, scratch, $"{id}.{PackageVersion}.nupkg");
            string extracted = Path.Combine(scratch, "build", ".NETFramework", frameworkVersion);
            if (!Directory.Exists(extracted))
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            Directory.Move(extracted, directory);
            return directory;
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}
