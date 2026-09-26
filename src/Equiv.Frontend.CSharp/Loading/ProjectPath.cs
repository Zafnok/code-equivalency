using System.Text;
using System.Text.RegularExpressions;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// Paths as a Windows project file writes them, resolved on any file system (M3-029): backslashes are separators, and
/// a path that does not exist as written is matched segment by segment, ignoring case. Wildcards follow MSBuild:
/// <c>*</c> and <c>?</c> within a segment, <c>**</c> for any number of directories.
/// </summary>
internal static class ProjectPath
{
    /// <summary><paramref name="path"/> made absolute against <paramref name="baseDirectory"/>, with this OS's separators.</summary>
    public static string Full(string baseDirectory, string path)
    {
        string native = path.Trim().Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(native) ? native : Path.Combine(baseDirectory, native));
    }

    /// <summary>The existing file or directory <paramref name="path"/> names, matched case-insensitively if need be, or null.</summary>
    public static string? Resolve(string baseDirectory, string path)
    {
        if (path.Trim().Length == 0)
        {
            return null;
        }

        string full = Full(baseDirectory, path);
        return File.Exists(full) || Directory.Exists(full) ? full : MatchIgnoringCase(full);
    }

    /// <summary>
    /// The existing path that <paramref name="full"/> names when each segment is matched ignoring case, or null. Only a
    /// case-sensitive file system gets here from <see cref="Resolve"/>; it is tested directly on every OS.
    /// </summary>
    internal static string? MatchIgnoringCase(string full)
    {
        string root = Path.GetPathRoot(full)!;
        string current = root;
        foreach (string segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string? match = Directory.Exists(current)
                ? Directory.EnumerateFileSystemEntries(current).FirstOrDefault(e => string.Equals(Path.GetFileName(e), segment, StringComparison.OrdinalIgnoreCase))
                : null;
            if (match is null)
            {
                return null;
            }

            current = match;
        }

        return current;
    }

    public static bool IsWildcard(string spec) => spec.AsSpan().IndexOfAny('*', '?') >= 0;

    /// <summary>The files a wildcard <paramref name="spec"/> matches under <paramref name="baseDirectory"/>, in ordinal order.</summary>
    public static IEnumerable<string> Glob(string baseDirectory, string spec)
    {
        string[] segments = spec.Trim().Replace('\\', '/').Split('/');
        int firstWild = Array.FindIndex(segments, IsWildcard);
        string fixedPart = string.Join('/', segments[..firstWild]);
        string? root = fixedPart.Length == 0 ? Full(baseDirectory, ".") : Resolve(baseDirectory, fixedPart);
        if (root is null || !Directory.Exists(root))
        {
            return [];
        }

        Regex pattern = Pattern(segments[firstWild..]);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => pattern.IsMatch(Path.GetRelativePath(root, f).Replace('\\', '/')))
            .Order(StringComparer.Ordinal);
    }

    private static Regex Pattern(string[] segments)
    {
        StringBuilder regex = new("^");
        for (int i = 0; i < segments.Length; i++)
        {
            bool last = i == segments.Length - 1;
            if (string.Equals(segments[i], "**", StringComparison.Ordinal))
            {
                regex.Append(last ? ".*" : "(?:[^/]*/)*");
                continue;
            }

            regex.Append(Regex.Escape(segments[i]).Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal));
            regex.Append(last ? string.Empty : "/");
        }

        return new Regex(regex.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
