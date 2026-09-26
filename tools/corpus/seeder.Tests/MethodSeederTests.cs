using System.Globalization;

using Equiv.Corpus.Seeder;

using Xunit;

namespace Equiv.Corpus.Seeder.Tests;

/// <summary>Ticket M4-010: <see cref="MethodSeeder"/> picks methods, applies one operator each, and drops what does not compile.</summary>
public sealed class MethodSeederTests
{
    private static readonly HashSet<string> NoChangedIdentities = new(StringComparer.Ordinal);

    /// <summary>Two files, each with one renameable local and nothing else mutable: exactly one seed lands in each.</summary>
    [Fact]
    public void Seeder_AppliesOneOperatorPerMethod()
    {
        using TempDirectory dir = new();
        dir.WriteFile("A.cs", TwoLocals("A"));
        dir.WriteFile("B.cs", TwoLocals("B"));

        MethodSeeder.SeedResult result = MethodSeeder.Seed(dir.Path, count: 10, seed: 1, NoChangedIdentities);

        Assert.Equal(2, result.Applied);
        Assert.Equal(0, result.Dropped);
        Assert.Equal(result.Seeds.Count, result.Seeds.Select(static s => s.File).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The only mutable site is <c>s = null;</c>: introducing a temporary makes it <c>var t = null;</c>, which does not
    /// compile (CS0815). The seeder must drop it, count it, and leave the file untouched.
    /// </summary>
    [Fact]
    public void Seeder_DropsUncompilableMutants()
    {
        using TempDirectory dir = new();
        const string Source = """
            public class Broken
            {
                public static void M(string s)
                {
                    s = null;
                }
            }
            """;
        dir.WriteFile("Broken.cs", Source);

        MethodSeeder.SeedResult result = MethodSeeder.Seed(dir.Path, count: 1, seed: 1, NoChangedIdentities);

        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Dropped);
        Assert.Empty(result.Seeds);
        Assert.Equal(Source, File.ReadAllText(Path.Combine(dir.Path, "Broken.cs")));
    }

    [Fact]
    public void Seeder_ManifestListsEverySeed()
    {
        using TempDirectory dir = new();
        for (int i = 0; i < 3; i++)
        {
            string name = Name(i);
            dir.WriteFile($"{name}.cs", TwoLocals(name));
        }

        MethodSeeder.SeedResult result = MethodSeeder.Seed(dir.Path, count: 3, seed: 42, NoChangedIdentities);

        Assert.Equal(3, result.Applied);
        Assert.Equal(3, result.Seeds.Count);
        foreach (MethodSeeder.SeedRecord seed in result.Seeds)
        {
            Assert.False(string.IsNullOrWhiteSpace(seed.Id));
            Assert.False(string.IsNullOrWhiteSpace(seed.Identity));
            Assert.False(string.IsNullOrWhiteSpace(seed.File));
            Assert.True(seed.Line > 0);
            Assert.True(Enum.IsDefined(seed.Operator));
        }
    }

    [Fact]
    public void Seeder_IsDeterministicForASeed()
    {
        using TempDirectory first = new();
        using TempDirectory second = new();
        for (int i = 0; i < 5; i++)
        {
            string name = Name(i);
            string source = Branching(name);
            first.WriteFile($"{name}.cs", source);
            second.WriteFile($"{name}.cs", source);
        }

        MethodSeeder.SeedResult a = MethodSeeder.Seed(first.Path, count: 3, seed: 7, NoChangedIdentities);
        MethodSeeder.SeedResult b = MethodSeeder.Seed(second.Path, count: 3, seed: 7, NoChangedIdentities);

        Assert.Equal(a.Requested, b.Requested);
        Assert.Equal(a.Applied, b.Applied);
        Assert.Equal(a.Dropped, b.Dropped);
        Assert.Equal(
            a.Seeds.Select(static s => (s.Id, s.Identity, s.File, s.Line, s.Operator)),
            b.Seeds.Select(static s => (s.Id, s.Identity, s.File, s.Line, s.Operator)));
    }

    private static string Name(int index) => "F" + index.ToString(CultureInfo.InvariantCulture);

    private static string TwoLocals(string type) => $$"""
        public class {{type}}
        {
            public static void M(int a, int b)
            {
                int x = a;
                int y = b;
            }
        }
        """;

    private static string Branching(string type) => $$"""
        public class {{type}}
        {
            public static void M(int a, int b)
            {
                int x = a;
                int y = b;
                if (x < y)
                {
                    x = y;
                }
            }
        }
        """;

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("equiv-seeder-test-").FullName;

        public void WriteFile(string relativePath, string contents) => File.WriteAllText(System.IO.Path.Combine(Path, relativePath), contents);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
