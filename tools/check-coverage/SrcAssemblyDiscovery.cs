namespace CheckCoverage;

internal static class SrcAssemblyDiscovery
{
    public static IReadOnlySet<string> DiscoverAssemblyNames(string srcDirectory)
    {
        if (!Directory.Exists(srcDirectory))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (string project in Directory.EnumerateFiles(srcDirectory, "*.csproj", SearchOption.AllDirectories))
        {
            string? name = Path.GetFileNameWithoutExtension(project);
            if (name is not null)
            {
                names.Add(name);
            }
        }

        return names;
    }
}
