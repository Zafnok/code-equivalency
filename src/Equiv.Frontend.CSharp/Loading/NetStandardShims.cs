namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// The .NET SDK's <c>Microsoft.NET.Build.Extensions/net461/lib</c> folder (M3-029): the <c>netstandard</c> shims MSBuild
/// adds for a .NET Framework 4.6.1 to 4.7 project that references a .NET Standard library. The SDK is found through
/// <c>$DOTNET_ROOT</c>, else the <c>dotnet</c> on the <c>PATH</c>; the newest SDK that has the folder wins.
/// </summary>
internal static class NetStandardShims
{
    public static string? Find(Func<string, string?> environment)
    {
        string? root = environment("DOTNET_ROOT") is { Length: > 0 } configured ? configured : DotnetOnPath(environment("PATH") ?? string.Empty);
        string sdks = root is null ? string.Empty : Path.Combine(root, "sdk");
        return !Directory.Exists(sdks)
            ? null
            : Directory.EnumerateDirectories(sdks)
                .Select(static sdk => (Version: SdkVersion(Path.GetFileName(sdk)), Shims: Path.Combine(sdk, "Microsoft", "Microsoft.NET.Build.Extensions", "net461", "lib")))
                .Where(static s => s.Version is not null && Directory.Exists(s.Shims))
                .OrderByDescending(static s => s.Version)
                .Select(static s => s.Shims)
                .FirstOrDefault();
    }

    /// <summary>The directory the first <c>dotnet</c> on <paramref name="path"/> really lives in, following a symbolic link.</summary>
    private static string? DotnetOnPath(string path)
    {
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string name in (string[])["dotnet", "dotnet.exe"])
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return Path.GetDirectoryName(File.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName ?? candidate);
                }
            }
        }

        return null;
    }

    private static Version? SdkVersion(string name) =>
        Version.TryParse(name.Split('-')[0], out Version? version) ? version : null;
}
