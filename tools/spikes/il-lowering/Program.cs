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
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IlLoweringSpike;

/// <summary>
/// P1-012: of the changed pairs still opaque after the kept M4 tickets, how many would an ILAst-based fallback lower with no
/// opaque node, and how often do the two compilers' ILAst differ for token-identical C#? Loads both solutions through the
/// production frontend, emits each project's compilation, and reads both methods of every such pair back as ILAst. Prints
/// counts, instruction kinds and opaque reasons; never source text.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length is not (2 or 3))
        {
            Console.Error.WriteLine("usage: il-lowering-spike <legacy.sln> <modern.sln> [<equiv.sarif from a full run of the same pair>]");
            return 2;
        }

        Dictionary<string, string>? verdicts = args.Length == 3 ? ReadSarif(args[2]) : null;
        Dictionary<IrProcedure, Side> sides = new(ReferenceEqualityComparer.Instance);
        ImmutableArray<ApiEquivalence> equivalences = ApiEquivalenceTable.Load().Enabled(EquivConfig.Default.SuppressApiEquivalences);
        CSharpFrontend frontend = new(
            CSharpFrontend.CreateLoader(OperatingSystem.IsWindows()),
            new StableIdentityMatcher(),
            (symbol, compilation, config, legacy) =>
            {
                (IrProcedure body, ImmutableArray<string> applied) = CSharpFrontend.LowerWithIrLowerer(symbol, compilation, config, legacy);
                (string? text, bool sensitive) = BodyFingerprinter.Text(symbol, compilation, config, legacy ? equivalences : [], legacy);
                sides[body] = new Side(symbol, compilation, text, sensitive);
                return (body, applied);
            });

        DateTime start = DateTime.UtcNow;
        FrontendAnalysis analysis = frontend.Analyze(args[0], args[1], EquivConfig.Default, NullRunLog.Instance, CancellationToken.None);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"loaded and lowered in {(DateTime.UtcNow - start).TotalSeconds:F0} s"));

        IlAstReader reader = new();
        Tally changed = new("changed pairs holding an unshared opaque (the measured population)");
        Tally sharedOnly = new("changed pairs whose only opaques are shared (M4-004), for reference");
        Tally identicalAll = new("every matched pair with token-identical C#, for reference (drift only)");
        Tally? unknownOpaque = verdicts is null ? null : new("the population's pairs a full run reported Unknown(opaque)");
        int changedPairs = 0;
        int changedWithoutOpaque = 0;
        int noText = 0;
        start = DateTime.UtcNow;
        foreach (ProcedurePair pair in analysis.Match.Pairs)
        {
            if (pair.OldBody is null || pair.NewBody is null || !sides.TryGetValue(pair.OldBody, out Side? old) || !sides.TryGetValue(pair.NewBody, out Side? @new)
                || old.Text is null || @new.Text is null)
            {
                noText++;
                continue;
            }

            bool congruent = string.Equals(old.Text, @new.Text, StringComparison.Ordinal) && !old.Sensitive && !@new.Sensitive;
            bool identicalCSharp = Tokens(old.Symbol) is { } oldTokens && string.Equals(oldTokens, Tokens(@new.Symbol), StringComparison.Ordinal);
            if (congruent)
            {
                if (identicalCSharp)
                {
                    identicalAll.Add(Measure(reader, pair, old, @new, identicalCSharp: true));
                }

                continue;
            }

            changedPairs++;
            PairMeasure measure = Measure(reader, pair, old, @new, identicalCSharp);
            if (identicalCSharp)
            {
                identicalAll.Add(measure);
            }

            if (measure.UnsharedReasons.Count > 0)
            {
                changed.Add(measure);
                if (verdicts?.GetValueOrDefault(pair.New.Value) == "EQ003/opaque")
                {
                    unknownOpaque!.Add(measure);
                }
            }
            else if (measure.HasOpaque)
            {
                sharedOnly.Add(measure);
            }
            else
            {
                changedWithoutOpaque++;
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"decompiled in {(DateTime.UtcNow - start).TotalSeconds:F0} s"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"matched pairs {analysis.Match.Pairs.Length}; lowering failures {analysis.Match.LoweringFailures.Length}; without a serialisation {noText}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"changed pairs {changedPairs}; with no opaque {changedWithoutOpaque}; only shared opaques {sharedOnly.Pairs}; holding an unshared opaque {changed.Pairs}"));
        Console.WriteLine();
        Console.WriteLine("## ILAst pipeline");
        Console.WriteLine(string.Join(" -> ", reader.Transforms));
        changed.Print(changedPairs, top: 20);
        unknownOpaque?.Print(changedPairs, top: 10);
        sharedOnly.Print(changedPairs, top: 10);
        identicalAll.Print(identicalAll.Pairs, top: 10);
        if (Environment.GetEnvironmentVariable("IL_SPIKE_DRIFT") is "1")
        {
            changed.PrintDrift();
            identicalAll.PrintDrift();
        }
        Console.WriteLine();
        Console.WriteLine("## mapping table");
        foreach ((string key, string ir) in MappingTable.Mapped.OrderBy(static e => e.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"| `{key}` | {ir} |");
        }

        return 0;
    }

    private static PairMeasure Measure(IlAstReader reader, ProcedurePair pair, Side old, Side @new, bool identicalCSharp)
    {
        List<IrOpaque> oldOpaques = Opaques(pair.OldBody!);
        List<IrOpaque> newOpaques = Opaques(pair.NewBody!);
        HashSet<string> unshared = new(StringComparer.Ordinal);
        Unshared(oldOpaques, newOpaques, unshared);
        Unshared(newOpaques, oldOpaques, unshared);

        bool stateMachine = old.Symbol.IsAsync || @new.Symbol.IsAsync || old.Symbol.IsIterator || @new.Symbol.IsIterator;
        (IlAst? oldAst, string? oldFailure) = reader.Read(old.Symbol, old.Compilation);
        (IlAst? newAst, string? newFailure) = reader.Read(@new.Symbol, @new.Compilation);
        return new PairMeasure(
            oldOpaques.Count + newOpaques.Count > 0,
            unshared,
            stateMachine,
            oldFailure ?? newFailure,
            oldAst,
            newAst,
            identicalCSharp)
        { Identity = pair.New.Value, SerialisationEqual = string.Equals(old.Text, @new.Text, StringComparison.Ordinal) };
    }

    private static void Unshared(List<IrOpaque> side, List<IrOpaque> other, HashSet<string> into)
    {
        HashSet<string> otherPrints = [.. other.Select(static o => o.Fingerprint).OfType<string>()];
        foreach (IrOpaque opaque in side.Where(o => o.Fingerprint is null || !otherPrints.Contains(o.Fingerprint)))
        {
            into.Add(opaque.Reason);
        }
    }

    private static List<IrOpaque> Opaques(IrProcedure body) => [.. body.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>()];

    /// <summary>
    /// The declaration's tokens, trivia dropped. A constructor's IL also holds its type's field and property initialisers, so a
    /// constructor is compared by its whole type declaration.
    /// </summary>
    private static string? Tokens(IMethodSymbol symbol)
    {
        IEnumerable<SyntaxNode> nodes = symbol.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
            ? symbol.ContainingType.DeclaringSyntaxReferences.Select(static r => r.GetSyntax()).OfType<TypeDeclarationSyntax>()
            : symbol.DeclaringSyntaxReferences.Select(static r => r.GetSyntax());
        List<string> tokens = [.. nodes.SelectMany(static n => n.DescendantTokens()).Select(static t => t.Text)];
        return tokens.Count == 0 ? null : string.Join('\u0001', tokens);
    }

    private static Dictionary<string, string> ReadSarif(string path)
    {
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(path));
        Dictionary<string, string> verdicts = new(StringComparer.Ordinal);
        foreach (JsonElement result in sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray())
        {
            if (!result.TryGetProperty("partialFingerprints", out JsonElement prints) || !prints.TryGetProperty("procedureIdentity/v1", out JsonElement identity))
            {
                continue;
            }

            string reason = result.TryGetProperty("properties", out JsonElement p) && p.TryGetProperty("unknownReason", out JsonElement r) ? r.GetString() ?? "" : "";
            verdicts[identity.GetString()!] = $"{result.GetProperty("ruleId").GetString()}/{reason}";
        }

        return verdicts;
    }
}

internal sealed record Side(IMethodSymbol Symbol, Compilation Compilation, string? Text, bool Sensitive);

internal sealed record PairMeasure(
    bool HasOpaque,
    IReadOnlySet<string> UnsharedReasons,
    bool StateMachine,
    string? Failure,
    IlAst? Old,
    IlAst? New,
    bool IdenticalCSharp)
{
    public string Identity { get; init; } = "";

    /// <summary>The two ADR 0024 serialisations are equal: IOperation lowering sees no difference between the bodies.</summary>
    public bool SerialisationEqual { get; init; }

    public IEnumerable<string> Unmapped =>
        Old!.Keys.Concat(New!.Keys).Where(static k => !MappingTable.Mapped.ContainsKey(k)).Distinct(StringComparer.Ordinal);
}

/// <summary>Counts for one population of pairs.</summary>
internal sealed class Tally(string name)
{
    private readonly Dictionary<string, int> unmapped = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> soleUnmapped = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> failures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> reasonsAll = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> reasonsLowerable = new(StringComparer.Ordinal);
    private int stateMachines;
    private int lowerable;
    private int notLowerable;
    private int identical;
    private int textDrift;
    private int shapeDrift;
    private int shapeDriftLowerable;
    private int textDriftLowerable;
    private int serialisationEqual;
    private int ilOnlyText;
    private int ilOnlyShape;
    private readonly List<(string Identity, string Old, string New)> drifts = [];
    private readonly List<(string Identity, string Old, string New)> ilOnly = [];

    public int Pairs { get; private set; }

    public void Add(PairMeasure pair)
    {
        Pairs++;
        foreach (string reason in pair.UnsharedReasons)
        {
            reasonsAll[reason] = reasonsAll.GetValueOrDefault(reason) + 1;
        }

        if (pair.StateMachine)
        {
            stateMachines++;
            return;
        }

        if (pair.Failure is not null)
        {
            failures[pair.Failure] = failures.GetValueOrDefault(pair.Failure) + 1;
            return;
        }

        List<string> missing = [.. pair.Unmapped];
        bool lowers = missing.Count == 0;
        if (lowers)
        {
            lowerable++;
            foreach (string reason in pair.UnsharedReasons)
            {
                reasonsLowerable[reason] = reasonsLowerable.GetValueOrDefault(reason) + 1;
            }
        }
        else
        {
            notLowerable++;
            foreach (string key in missing)
            {
                unmapped[key] = unmapped.GetValueOrDefault(key) + 1;
            }

            if (missing.Count == 1)
            {
                soleUnmapped[missing[0]] = soleUnmapped.GetValueOrDefault(missing[0]) + 1;
            }
        }

        if (pair.IdenticalCSharp && pair.SerialisationEqual)
        {
            serialisationEqual++;
            if (!string.Equals(pair.Old!.Text, pair.New!.Text, StringComparison.Ordinal))
            {
                ilOnlyText++;
                ilOnly.Add((pair.Identity, pair.Old.Text, pair.New.Text));
                ilOnlyShape += string.Equals(pair.Old.Shape, pair.New.Shape, StringComparison.Ordinal) ? 0 : 1;
            }
        }

        if (pair.IdenticalCSharp)
        {
            identical++;
            if (!string.Equals(pair.Old!.Text, pair.New!.Text, StringComparison.Ordinal))
            {
                textDrift++;
                textDriftLowerable += lowers ? 1 : 0;
                if (!string.Equals(pair.Old.Shape, pair.New.Shape, StringComparison.Ordinal))
                {
                    shapeDrift++;
                    shapeDriftLowerable += lowers ? 1 : 0;
                    drifts.Add((pair.Identity, pair.Old.Shape, pair.New.Shape));
                }
            }
        }
    }

    public void Print(int changedPairs, int top)
    {
        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"## {name}: {Pairs} pairs"));
        Console.WriteLine($"| bucket | pairs | share of {changedPairs} |");
        Row("async or iterator state machine (either side)", stateMachines, changedPairs);
        foreach ((string failure, int count) in failures.OrderByDescending(static e => e.Value))
        {
            Row($"no ILAst: {failure}", count, changedPairs);
        }

        Row("**lowerable: only mapped instruction kinds, both sides**", lowerable, changedPairs);
        Row("not lowerable: some unmapped kind", notLowerable, changedPairs);
        Row("token-identical C# (of the decompiled pairs)", identical, changedPairs);
        Row("... ILAst text differs (offsets, labels and variable names canonical)", textDrift, changedPairs);
        Row("...... of which lowerable", textDriftLowerable, changedPairs);
        Row("... **instruction tree differs (compiler shape drift)**", shapeDrift, changedPairs);
        Row("...... of which lowerable", shapeDriftLowerable, changedPairs);
        Row("token-identical C# and equal ADR 0024 serialisations (IOperation sees no difference)", serialisationEqual, changedPairs);
        Row("... ILAst text differs anyway (drift only IL introduces)", ilOnlyText, changedPairs);
        Row("... instruction tree differs anyway", ilOnlyShape, changedPairs);
        Print($"unmapped kinds (pairs holding each, of {notLowerable})", unmapped, top);
        Print("unmapped kinds that are a pair's only one (pairs)", soleUnmapped, top);
        Print("unshared opaque reasons (pairs holding each)", reasonsAll, top);
        Print("unshared opaque reasons in the lowerable pairs (pairs holding each)", reasonsLowerable, top);
    }

    /// <summary>Each shape-drift pair's identity and the opcode trees around the first difference: opcodes only, no operand.</summary>
    public void PrintDrift()
    {
        Console.WriteLine();
        Console.WriteLine($"## shape drift in {name}");
        Differences(drifts);
        Console.WriteLine();
        Console.WriteLine($"## drift only IL introduces in {name} (ILAst text: kept in .corpus, never committed)");
        Differences(ilOnly);
    }

    private static void Differences(List<(string Identity, string Old, string New)> pairs)
    {
        foreach ((string identity, string old, string @new) in pairs)
        {
            int at = 0;
            while (at < old.Length && at < @new.Length && old[at] == @new[at])
            {
                at++;
            }

            int from = Math.Max(0, at - 60);
            Console.WriteLine($"- {identity}");
            Console.WriteLine($"  legacy ...{old[from..Math.Min(old.Length, at + 120)]}");
            Console.WriteLine($"  modern ...{@new[from..Math.Min(@new.Length, at + 120)]}");
        }
    }

    private static void Row(string label, int count, int changedPairs) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {label} | {count} | {100.0 * count / Math.Max(changedPairs, 1):F1}% |"));

    private static void Print(string title, Dictionary<string, int> counts, int top)
    {
        Console.WriteLine($"### {title}");
        foreach ((string key, int count) in counts.OrderByDescending(static e => e.Value).ThenBy(static e => e.Key, StringComparer.Ordinal).Take(top))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| `{key}` | {count} |"));
        }
    }
}
