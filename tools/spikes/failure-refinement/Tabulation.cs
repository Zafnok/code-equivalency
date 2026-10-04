using System.Text.Json;

namespace FailureRefinementSpike;

/// <summary>One Unknown (EQ003) of a run: its reason and, where the backend asked, ADR 0037's two outcomes.</summary>
internal sealed record UnknownResult(string Identity, string Reason, string? NewFailures, string? RemovedFailures);

/// <summary>
/// Criterion 1: <c>properties.failureRefinement</c> of every Unknown in a run's SARIF, by outcome and by
/// <c>unknownReason</c>.
/// </summary>
internal static class Tabulation
{
    public const string NotQueried = "not queried";

    private static readonly string[] Outcomes = ["none-proved", "found", "unknown", NotQueried];

    public static void Print(string[] sarifPaths)
    {
        List<(string Run, List<UnknownResult> Unknowns)> runs = [.. sarifPaths.Select(static p => (Label(p), Read(p)))];
        foreach ((string run, List<UnknownResult> unknowns) in runs)
        {
            Console.WriteLine();
            Console.WriteLine($"## {run}: {unknowns.Count} Unknowns, {unknowns.Count(static u => u.NewFailures is not null)} queried");
            Table("newFailures", unknowns, static u => u.NewFailures);
            Table("removedFailures", unknowns, static u => u.RemovedFailures);
        }

        List<UnknownResult> all = [.. runs.SelectMany(static r => r.Unknowns)];
        Console.WriteLine();
        Console.WriteLine($"## All {runs.Count} runs: {all.Count} Unknowns, {all.Count(static u => u.NewFailures is not null)} queried");
        Table("newFailures", all, static u => u.NewFailures);
        Table("removedFailures", all, static u => u.RemovedFailures);

        int queried = all.Count(static u => u.NewFailures is not null);
        int noneProved = all.Count(static u => u.NewFailures == "none-proved");
        int both = all.Count(static u => u.NewFailures == "none-proved" && u.RemovedFailures == "none-proved");
        Console.WriteLine();
        Console.WriteLine($"queried Unknowns with a none-proved newFailures: {noneProved} of {queried} ({Program.Share(noneProved, queried)}); with both none-proved: {both} ({Program.Share(both, queried)})");
    }

    /// <summary>The Unknowns of the run at <paramref name="path"/>.</summary>
    public static List<UnknownResult> Read(string path)
    {
        // .corpus/ SARIF starts with a UTF-8 BOM, which JsonDocument.Parse(byte[]) rejects (P1-011); read it as text.
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(path));
        List<UnknownResult> unknowns = [];
        foreach (JsonElement result in sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray())
        {
            if (!string.Equals(result.GetProperty("ruleId").GetString(), "EQ003", StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement properties = result.GetProperty("properties");
            bool queried = properties.TryGetProperty("failureRefinement", out JsonElement refinement);
            unknowns.Add(new UnknownResult(
                result.GetProperty("partialFingerprints").GetProperty("procedureIdentity/v1").GetString()!,
                properties.GetProperty("unknownReason").GetString()!,
                queried ? refinement.GetProperty("newFailures").GetProperty("outcome").GetString() : null,
                queried ? refinement.GetProperty("removedFailures").GetProperty("outcome").GetString() : null));
        }

        return unknowns;
    }

    /// <summary>The pair's slug when the SARIF is at <c>.corpus/pairs/&lt;slug&gt;/runs/&lt;run&gt;/equiv.sarif</c>, else its path.</summary>
    private static string Label(string path)
    {
        DirectoryInfo? run = new FileInfo(path).Directory;
        return run?.Parent is { Name: "runs", Parent: { } pair } ? $"{pair.Name} ({run.Name})" : path;
    }

    private static void Table(string query, List<UnknownResult> unknowns, Func<UnknownResult, string?> outcome)
    {
        Console.WriteLine();
        Console.WriteLine($"| `{query}` by `unknownReason` | Unknowns | {string.Join(" | ", Outcomes)} |");
        Console.WriteLine($"|---|---|{string.Concat(Enumerable.Repeat("---|", Outcomes.Length))}");
        foreach (IGrouping<string, UnknownResult> reason in unknowns.GroupBy(static u => u.Reason).OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"| {reason.Key} | {reason.Count()} | {Cells([.. reason])} |");
        }

        Console.WriteLine($"| **all** | {unknowns.Count} | {Cells(unknowns)} |");

        string Cells(List<UnknownResult> rows) =>
            string.Join(" | ", Outcomes.Select(o => rows.Count(r => string.Equals(outcome(r) ?? NotQueried, o, StringComparison.Ordinal))));
    }
}
