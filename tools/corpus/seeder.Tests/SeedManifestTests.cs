using Equiv.Corpus.Seeder;

using Xunit;

namespace Equiv.Corpus.Seeder.Tests;

/// <summary>Ticket P2-063: a seed's <c>line</c> in the manifest is the line the mutation changed, not where its method starts.</summary>
public sealed class SeedManifestTests
{
    /// <summary>
    /// The method starts on line 3 of the file and its only mutable site, whichever operator the seeder draws, is the
    /// <c>return</c> on the method's fourth line, line 6 of the file.
    /// </summary>
    [Fact]
    public void LineIsTheMutatedLine()
    {
        string root = Directory.CreateTempSubdirectory("equiv-seeder-test-").FullName;
        try
        {
            const string Source = """
                public class Fourth
                {
                    public static int M(int a, int b)
                    {
                        System.Console.WriteLine();
                        return a < b ? a : b;
                    }
                }
                """;
            File.WriteAllText(Path.Combine(root, "Fourth.cs"), Source);

            MethodSeeder.SeedResult result = MethodSeeder.Seed(root, count: 1, seed: 1, new HashSet<string>(StringComparer.Ordinal));

            MethodSeeder.SeedRecord seed = Assert.Single(result.Seeds);
            Assert.Equal(6, seed.Line);
            string[] before = Source.Split('\n');
            string[] after = File.ReadAllText(Path.Combine(root, "Fourth.cs")).Split('\n');
            Assert.Equal(string.Join('\n', before[..5]), string.Join('\n', after[..5]));
            Assert.False(string.Equals(before[5], after[5], StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
