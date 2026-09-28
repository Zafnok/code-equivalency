using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Frontend.CSharp;
using Equiv.Frontend.CSharp.Fingerprinting;

using Microsoft.CodeAnalysis;

namespace EgraphSpike;

/// <summary>
/// P1-011: of the changed pairs a full run left Unknown(opaque), how many differ only where the fixed rule set closes the
/// difference? Loads both solutions through the production frontend, keeps each lowered body's ADR 0024 serialisation, and
/// joins the pairs to the run's SARIF by procedure identity. Prints counts, rule names and node kinds; never source text.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            return SelfTest.Run();
        }

        if (args.Length is not (2 or 3))
        {
            Console.Error.WriteLine("usage: egraph-spike <legacy.sln> <modern.sln> [<equiv.sarif from a full run of the same pair>]");
            return 2;
        }

        // Without a SARIF file every changed pair is measured, split by whether it holds an opaque at all: an upper bound.
        (int ChangedPairs, Dictionary<string, Verdict> Verdicts)? run = args.Length == 3 ? ReadSarif(args[2]) : null;
        Dictionary<IrProcedure, (string Text, bool RuntimeSensitive)> texts = new(ReferenceEqualityComparer.Instance);
        ImmutableArray<ApiEquivalence> equivalences = ApiEquivalenceTable.Load().Enabled(EquivConfig.Default.SuppressApiEquivalences);
        CSharpFrontend frontend = new(
            CSharpFrontend.CreateLoader(OperatingSystem.IsWindows()),
            new StableIdentityMatcher(),
            (symbol, compilation, config, legacy) =>
            {
                (IrProcedure body, ImmutableArray<string> applied) = CSharpFrontend.LowerWithIrLowerer(symbol, compilation, config, legacy);
                if (BodyFingerprinter.Text(symbol, compilation, config, legacy ? equivalences : [], legacy) is ({ } text, bool sensitive))
                {
                    texts[body] = (text, sensitive);
                }

                return (body, applied);
            });

        FrontendAnalysis analysis = frontend.Analyze(args[0], args[1], EquivConfig.Default, NullRunLog.Instance, CancellationToken.None);
        Dictionary<string, Tally> tallies = new(StringComparer.Ordinal);
        int changed = 0;
        int noText = 0;
        foreach (ProcedurePair pair in analysis.Match.Pairs)
        {
            if (pair.OldBody is null || pair.NewBody is null || !texts.TryGetValue(pair.OldBody, out var old) || !texts.TryGetValue(pair.NewBody, out var @new))
            {
                noText++;
                continue;
            }

            if (string.Equals(old.Text, @new.Text, StringComparison.Ordinal) && !old.RuntimeSensitive && !@new.RuntimeSensitive)
            {
                continue; // congruent (ADR 0024): not a changed pair
            }

            changed++;
            Verdict? verdict = run?.Verdicts.GetValueOrDefault(pair.New.Value);
            string population = run is null
                ? HasOpaque(pair.OldBody) || HasOpaque(pair.NewBody) ? "changed, holding an opaque" : "changed, no opaque"
                : verdict is { RuleId: "EQ003", UnknownReason: "opaque" } ? "Unknown(opaque)" : $"other ({verdict?.RuleId ?? "no result"})";
            if (!tallies.TryGetValue(population, out Tally? tally))
            {
                tallies[population] = tally = new Tally(population);
            }

            tally.Add(old, @new, verdict);
        }

        int denominator = run?.ChangedPairs ?? changed;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"matched pairs {analysis.Match.Pairs.Length}; without a serialisation {noText}; changed (spike) {changed}; changed (run.properties.loweringCensus) {run?.ChangedPairs.ToString(CultureInfo.InvariantCulture) ?? "n/a"}"));
        foreach (Tally tally in tallies.Values.OrderBy(static t => t.Name, StringComparer.Ordinal))
        {
            tally.Print(denominator);
        }

        return 0;
    }

    private static bool HasOpaque(IrProcedure body) => body.Blocks.Any(static b => b.Instructions.Any(static i => i is IrOpaque));

    private static (int ChangedPairs, Dictionary<string, Verdict> Verdicts) ReadSarif(string path)
    {
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement run = sarif.RootElement.GetProperty("runs")[0];
        int changed = run.GetProperty("properties").GetProperty("loweringCensus").GetProperty("changedPairs").GetInt32();
        Dictionary<string, Verdict> verdicts = new(StringComparer.Ordinal);
        foreach (JsonElement result in run.GetProperty("results").EnumerateArray())
        {
            if (!result.TryGetProperty("partialFingerprints", out JsonElement prints) || !prints.TryGetProperty("procedureIdentity/v1", out JsonElement identity))
            {
                continue;
            }

            JsonElement properties = result.TryGetProperty("properties", out JsonElement p) ? p : default;
            verdicts[identity.GetString()!] = new Verdict(result.GetProperty("ruleId").GetString()!, Property(properties, "unknownReason"), Property(properties, "proofMethod"));
        }

        return (changed, verdicts);
    }

    private static string? Property(JsonElement properties, string name) =>
        properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;
}

internal sealed record Verdict(string RuleId, string? UnknownReason, string? ProofMethod);

/// <summary>Counts for one population of pairs.</summary>
internal sealed class Tally(string name)
{
    public string Name => name;

    private readonly Dictionary<string, int> rules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> residuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> partial = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> closedByResult = new(StringComparer.Ordinal);
    private int pairs;
    private int closed;
    private int closedRuntimeSensitive;
    private int identical;

    public void Add((string Text, bool RuntimeSensitive) old, (string Text, bool RuntimeSensitive) @new, Verdict? verdict)
    {
        pairs++;
        if (string.Equals(old.Text, @new.Text, StringComparison.Ordinal))
        {
            identical++;
            return;
        }

        Outcome outcome = Differ.Close(OpTree.Parse(old.Text), OpTree.Parse(@new.Text));
        if (!outcome.Closed)
        {
            foreach (string residual in outcome.Residuals.Distinct(StringComparer.Ordinal))
            {
                residuals[residual] = residuals.GetValueOrDefault(residual) + 1;
            }

            foreach (string rule in outcome.Rules)
            {
                partial[rule] = partial.GetValueOrDefault(rule) + 1;
            }

            return;
        }

        if (old.RuntimeSensitive || @new.RuntimeSensitive)
        {
            closedRuntimeSensitive++;
            return;
        }

        closed++;
        string result = verdict is null ? "n/a" : verdict.UnknownReason is null ? $"{verdict.RuleId}({verdict.ProofMethod})" : $"{verdict.RuleId}({verdict.UnknownReason})";
        closedByResult[result] = closedByResult.GetValueOrDefault(result) + 1;
        foreach (string rule in outcome.Rules)
        {
            rules[rule] = rules.GetValueOrDefault(rule) + 1;
        }
    }

    public void Print(int changedPairs)
    {
        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"## {name}: {pairs} pairs"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"closed by the rule set: {closed} ({100.0 * closed / Math.Max(changedPairs, 1):F2}% of changed pairs)"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"closed but runtime-sensitive (still not congruent): {closedRuntimeSensitive}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"identical serialisations (runtime-sensitive): {identical}"));
        Print("closed, by result", closedByResult, int.MaxValue);
        Print("closing rules (pairs using each)", rules, 10);
        Print("residual differences (pairs holding each)", residuals, int.TryParse(Environment.GetEnvironmentVariable("EGRAPH_SPIKE_TOP"), out int top) ? top : 10);
        Print("rules that closed some difference in a pair that stays open (pairs using each)", partial, 10);
    }

    private static void Print(string title, Dictionary<string, int> counts, int top)
    {
        Console.WriteLine($"### {title}");
        foreach ((string key, int count) in counts.OrderByDescending(static e => e.Value).ThenBy(static e => e.Key, StringComparer.Ordinal).Take(top))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {key} | {count} |"));
        }
    }
}
