using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Equiv.Corpus.Seeder;

/// <summary>
/// Applies mechanical seeds across every method under a directory (ticket M4-010): picks methods at random, weighted
/// towards <paramref name="changedIdentities"/> when given any, applies one <see cref="MutationOperator"/> per method
/// and drops (but counts) an application <see cref="CompileCheck"/> rejects. At most one seed per file, so a compile
/// failure is always attributable to the one seed that file got, with no need to rebuild to find out which.
/// </summary>
internal static class MethodSeeder
{
    public static SeedResult Seed(string root, int count, int seed, IReadOnlySet<string> changedIdentities)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(changedIdentities);

        List<Candidate> candidates = [.. Discover(root).OrderBy(static c => c.RelativePath, StringComparer.Ordinal).ThenBy(static c => c.Method.SpanStart)];
        Random random = new(seed);
        List<Candidate> priority = WeightedOrder(candidates, changedIdentities, random);

        List<SeedRecord> applied = [];
        HashSet<string> seededFiles = new(StringComparer.OrdinalIgnoreCase);
        int dropped = 0;
        MutationOperator[] operators = Enum.GetValues<MutationOperator>();

        foreach (Candidate candidate in priority)
        {
            if (applied.Count >= count)
            {
                break;
            }

            if (seededFiles.Contains(candidate.RelativePath))
            {
                continue;
            }

            (MutationOperator Operator, int Site)? choice = ChooseOperator(candidate.Method, Shuffled(operators, random), random);
            if (choice is not { } picked)
            {
                continue;
            }

            MethodDeclarationSyntax? mutated = SyntaxMutator.Apply(picked.Operator, candidate.Method, picked.Site);
            if (mutated is null)
            {
                continue;
            }

            string mutatedText = candidate.FileRoot.ReplaceNode(candidate.Method, mutated).ToFullString();
            if (!CompileCheck.StillCompiles(candidate.OriginalText, mutatedText))
            {
                dropped++;
                continue;
            }

            File.WriteAllText(candidate.AbsolutePath, mutatedText);
            seededFiles.Add(candidate.RelativePath);
            applied.Add(new SeedRecord($"S{(applied.Count + 1).ToString("D3", System.Globalization.CultureInfo.InvariantCulture)}", candidate.Identity, candidate.RelativePath, candidate.Line, picked.Operator));
        }

        return new SeedResult(count, applied.Count, dropped, applied);
    }

    private static (MutationOperator Operator, int Site)? ChooseOperator(MethodDeclarationSyntax method, IReadOnlyList<MutationOperator> order, Random random)
    {
        foreach (MutationOperator op in order)
        {
            int sites = SyntaxMutator.Sites(op, method);
            if (sites > 0)
            {
                return (op, random.Next(sites));
            }
        }

        return null;
    }

    /// <summary>
    /// Assigns each candidate an Efraimidis-Spirakis key (<c>U^(1/weight)</c>, <c>U</c> uniform in (0,1]) and orders
    /// by it descending: equivalent to weighted sampling without replacement, and deterministic given <paramref name="random"/>'s
    /// seed and the candidates' fixed input order. A changed identity weighs 5x an unchanged one; with no changed
    /// identities at all, every weight is 1 (uniform), matching "weighted towards changed pairs when the census lists any".
    /// </summary>
    private static List<Candidate> WeightedOrder(List<Candidate> candidates, IReadOnlySet<string> changedIdentities, Random random) =>
        [.. candidates
            .Select(c => (Key: Math.Pow(1.0 - random.NextDouble(), 1.0 / (changedIdentities.Count > 0 && changedIdentities.Contains(c.Identity) ? 5.0 : 1.0)), Candidate: c))
            .OrderByDescending(static t => t.Key)
            .Select(static t => t.Candidate)];

    private static MutationOperator[] Shuffled(MutationOperator[] source, Random random)
    {
        MutationOperator[] copy = [.. source];
        for (int i = copy.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }

    private static IEnumerable<Candidate> Discover(string root)
    {
        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(static segment => segment is "bin" or "obj"))
            {
                continue;
            }

            string text = File.ReadAllText(path);
            SyntaxNode fileRoot = CSharpSyntaxTree.ParseText(text, path: path, cancellationToken: CancellationToken.None).GetRoot();
            string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            foreach (MethodDeclarationSyntax method in fileRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(static m => m.Body is not null))
            {
                int line = method.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                yield return new Candidate(relative, path, fileRoot, method, MethodIdentity.Of(method), line, text);
            }
        }
    }

    private sealed record Candidate(string RelativePath, string AbsolutePath, SyntaxNode FileRoot, MethodDeclarationSyntax Method, string Identity, int Line, string OriginalText);

    internal sealed record SeedRecord(string Id, string Identity, string File, int Line, MutationOperator Operator);

    internal sealed record SeedResult(int Requested, int Applied, int Dropped, IReadOnlyList<SeedRecord> Seeds);
}
