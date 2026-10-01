using System.Collections.Immutable;
using System.Globalization;

using CsCheck;

using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Corpus.Seeder;
using Equiv.Frontend.CSharp.Lowering.Il;
using Equiv.TestSupport;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// The differential soundness gate (VERIFICATION-MODEL.md section 7; ticket M0-012): generated C# pairs, executed on
/// the CLR, never contradict the verdict the real frontend and <see cref="Equiv.Verify.Z3.Z3Backend"/> give them. It
/// is EMI's metamorphic idea (Le, Afshari and Su, PLDI 2014) pointed at the checker. Each pair is run on
/// <see cref="InputsPerPair"/> CsCheck inputs and on its Divergent model, if it has one, and must satisfy:
/// <list type="number">
/// <item>soundness: if any input gives different observables, the verdict is not Equivalent;</item>
/// <item>decoding: if the verdict is Divergent, replaying its model in C# gives different observables;</item>
/// <item>precision: if the operator is preserving, the verdict is not Divergent.</item>
/// </list>
/// A failure prints the shrunk pair as its two classes, the operator, the input and both observables, which is a
/// ready-made regression test. <see cref="Budget"/> sets the pair count and the seed. Each pair is verified twice, once
/// with both sides lowered from IOperation and once with both forced through the IL lowering (ADR 0039; ticket P1-017),
/// and each verdict is held to all three rules; a failure names the rule and the lowering that broke it.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DifferentialSoundnessTests
{
    private const int InputsPerPair = 20;

    /// <summary>
    /// Pairs per pull request and per nightly run, and the fixed seed (acceptance criterion 3). The nightly job sets
    /// <c>EQUIV_DIFFERENTIAL_BUDGET=nightly</c>; <c>EQUIV_DIFFERENTIAL_SEED</c> overrides the seed, to replay a failure.
    /// </summary>
    internal static readonly (int PullRequest, int Nightly, string Seed) Budget = (200, 5_000, "000000000000");

    /// <summary>
    /// Failures a rule tolerates until the P2 ticket named beside each is fixed (acceptance criterion 7), matched by a
    /// symptom in the failure's input line rather than by pair, so the nightly run skips every pair the bug shows in and
    /// not only the one it shrank to. Must be empty before M3-003 lands.
    /// </summary>
    private static readonly ImmutableArray<(string Symptom, string Ticket)> Skips = [];

    /// <summary>
    /// The pairs the gate draws (ticket P1-017): one in four holds a construct the IL fallback exists for, which the
    /// IOperation lowering leaves opaque.
    /// </summary>
    internal static readonly Gen<(string LegacySource, string ModernSource, MutationOperator Operator)> Generated =
        Gen.Frequency((3, PairGen.Pair), (1, PairGen.IlPair));

    private static readonly ImmutableArray<PairRuntime.Lowering> BothLowerings = [PairRuntime.Lowering.Operation, PairRuntime.Lowering.Il];

    private static readonly ImmutableArray<Rule> Rules = [new(1, Soundness), new(2, Decoding), new(3, Precision)];

    /// <summary>
    /// The IL lowering with <c>!=</c> read as <c>==</c>: a mapping bug under which a flipped equality looks Equivalent while
    /// every input that reaches it runs differently, which rule 1 must catch.
    /// </summary>
    private static readonly PairRuntime.Lowering BrokenIl = new("il with != read as ==", static (method, compilation) =>
        IlLowerer.Lower(method, compilation, x87: false, static op => op switch
        {
            IrBinaryOp.Ne => IrBinaryOp.Eq,
            _ => op,
        }));

    /// <summary>The legacy side of the pair <see cref="ADerivationThatNeverReadsALengthReplaysAsDivergence"/> pins.</summary>
    private const string NeverReadsLengthLegacy = """
        public static class Oracle
        {
            public static int F;

            public static bool M(int a, int b, long c, long d, bool e, string s, int[] u)
            {
                int x = a;
                long y = c;
                bool z = e;
                int k0 = 0;
                while (e && k0 < 2)
                {
                    k0++;
                    y = d;
                }
                for (int i1 = 0; i1 < 3; i1++)
                {
                    if (((e & z) || (s != null)))
                    {
                        F = unchecked((int)y);
                    }
                    else
                    {
                        x = (e ? b : a);
                        y = c;
                        F = checked(unchecked(u[0] ^ x) / (33));
                    }
                }
                F = x;
                return (z && (s == null));
            }
        }

        """;

    /// <summary>Its modern side: <c>F = x;</c> dropped.</summary>
    private const string NeverReadsLengthModern = """
        public static class Oracle
        {
            public static int F;

            public static bool M(int a, int b, long c, long d, bool e, string s, int[] u)
            {
                int x = a;
                long y = c;
                bool z = e;
                int k0 = 0;
                while (e && k0 < 2)
                {
                    k0++;
                    y = d;
                }
                for (int i1 = 0; i1 < 3; i1++)
                {
                    if (((e & z) || (s != null)))
                    {
                        F = unchecked((int)y);
                    }
                    else
                    {
                        x = (e ? b : a);
                        y = c;
                        F = checked(unchecked(u[0] ^ x) / (33));
                    }
                }
                return (z && (s == null));
            }
        }

        """;

    private static int Pairs =>
        string.Equals(Environment.GetEnvironmentVariable("EQUIV_DIFFERENTIAL_BUDGET"), "nightly", StringComparison.OrdinalIgnoreCase) ? Budget.Nightly : Budget.PullRequest;

    private static string Seed => Environment.GetEnvironmentVariable("EQUIV_DIFFERENTIAL_SEED") is { Length: > 0 } seed ? seed : Budget.Seed;

    /// <summary>Rules 1 to 3, under the IOperation lowering and under the IL lowering.</summary>
    [Fact]
    public void GeneratedPairsAreSoundUnderBothLowerings() => Assert.Null(Failed(BothLowerings, Rules));

    /// <summary>A deliberately broken IL mapping fails rule 1 within the pull-request budget, and the failure prints its seed.</summary>
    [Fact]
    public void ABrokenIlMappingIsCaught()
    {
        Exception? failure = Failed([BrokenIl], [Rules[0]]);

        Assert.NotNull(failure);
        Assert.Contains("rule 1 under the il with != read as == lowering", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Set seed", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-043: rule 2 at CsCheck seed <c>6rdKklVqtDVa</c>, shrunk. Spacer's derivation of the dropped
    /// <c>F = x;</c> never reads <c>u</c>'s length, so it left it at -1, which no C# run can pass.
    /// </summary>
    [Fact]
    public void ADerivationThatNeverReadsALengthReplaysAsDivergence()
    {
        Case c = new(NeverReadsLengthLegacy, NeverReadsLengthModern, MutationOperator.DropFieldWrite, []);

        Assert.Null(Decoding(c, PairRuntime.Lowering.Operation)?.Describe(c));
    }

    /// <summary>
    /// Ticket P2-060 (ADR 0041). The IL mode of the gate lowered <c>$"{s}t"</c> to <c>String.Concat(string, string)</c>, and
    /// the heap model let that call write <c>F</c> and <c>u</c>, so a loop reading both after it diverged on a heap no real
    /// <c>Concat</c> leaves, and the model did not replay. <c>Concat</c> is closed, so reading <c>F</c> before or after it
    /// is one behaviour, from IL and, written <c>s + "t"</c>, from IOperation.
    /// </summary>
    [Fact]
    public void ABclCallCannotWriteAUserField()
    {
        Assert.IsType<Equivalent>(PairRuntime.Analyse(ReadAcrossConcat(readFirst: true, "$\"{s}t\""), ReadAcrossConcat(readFirst: false, "$\"{s}t\""), PairRuntime.Lowering.Il).Verdict);
        Assert.IsType<Equivalent>(PairRuntime.Analyse(ReadAcrossConcat(readFirst: true, "s + \"t\""), ReadAcrossConcat(readFirst: false, "s + \"t\""), PairRuntime.Lowering.Operation).Verdict);
    }

    /// <summary>
    /// A method that reads <c>F</c> before or after it compares <paramref name="concat"/> with null, then adds <c>u[0]</c>
    /// to what it read in a loop.
    /// </summary>
    private static string ReadAcrossConcat(bool readFirst, string concat)
    {
        const string read = "long x = F;";
        string call = $"bool z = ({concat} == null);";
        return $$"""
            public static class Oracle
            {
                public static int F;

                public static long M(int a, int b, long c, long d, bool e, string s, int[] u)
                {
                    {{(readFirst ? read : call)}}
                    {{(readFirst ? call : read)}}
                    for (int i0 = 0; i0 < 2; i0++)
                    {
                        x = unchecked(x + u[0]);
                    }

                    return (z ? c : x);
                }
            }

            """;
    }

    /// <summary>CsCheck reports a counter-example by throwing; surfacing it as a value gives each test its assertion.</summary>
    private static Exception? Failed(ImmutableArray<PairRuntime.Lowering> lowerings, ImmutableArray<Rule> rules) => Record.Exception(() => Sample(lowerings, rules));

    private static void Sample(ImmutableArray<PairRuntime.Lowering> lowerings, ImmutableArray<Rule> rules) =>
        Gen.Select(Generated, PairGen.Input.Array[InputsPerPair], static (pair, inputs) => new Case(pair.LegacySource, pair.ModernSource, pair.Operator, inputs))
            .Sample(c => Check(c, lowerings, rules) is null, seed: Seed, iter: Pairs, print: c => Check(c, lowerings, rules)?.Describe(c) ?? string.Empty);

    /// <summary>The first failure of <paramref name="c"/> that no <see cref="Skips"/> entry tolerates, lowering by lowering and rule by rule.</summary>
    private static Failure? Check(Case c, ImmutableArray<PairRuntime.Lowering> lowerings, ImmutableArray<Rule> rules) =>
        lowerings.SelectMany(lowering => rules.Select(rule => rule.Check(c, lowering) is { } failure ? failure with { Broken = string.Create(CultureInfo.InvariantCulture, $"rule {rule.Number} under the {lowering} lowering") } : null))
            .FirstOrDefault(static failure => failure is not null && !Skipped(failure));

    private static bool Skipped(Failure failure) => Skips.Any(s => failure.Input.Contains(s.Symptom, StringComparison.Ordinal));

    private static Failure? Soundness(Case c, PairRuntime.Lowering lowering)
    {
        PairRuntime.Analysis analysis = PairRuntime.Analyse(c.Legacy, c.Modern, lowering);
        if (analysis.Verdict is not Equivalent)
        {
            return null;
        }

        using PairRuntime.Loaded loaded = new(analysis);
        foreach (PairInput input in c.Inputs)
        {
            (string legacy, string modern) = loaded.Observe(input);
            if (!string.Equals(legacy, modern, StringComparison.Ordinal))
            {
                return new Failure(input.ToString(), legacy, modern);
            }
        }

        return null;
    }

    /// <summary>
    /// Rule 2. A model whose run makes an ordinary call also chose that call's result, <c>threw</c> flag and heap writes,
    /// which ADR 0026 leaves untainted (its 2026-09-30 clarifications) and which no C# argument can impose, so a replay of
    /// such a model that does not diverge is not a decoding failure. A closed call (ADR 0041) writes no heap, but the
    /// model still chooses its result and <c>threw</c> flag, so this holds for it too (ticket P2-060). A changing pair
    /// makes a call only through the IL fallback's constructs (<see cref="PairGen.IlPair"/>), and this applies to either
    /// lowering alike.
    /// </summary>
    private static Failure? Decoding(Case c, PairRuntime.Lowering lowering)
    {
        PairRuntime.Analysis analysis = PairRuntime.Analyse(c.Legacy, c.Modern, lowering);
        if (analysis.Verdict is not Divergent { Counterexample: var counterexample })
        {
            return null;
        }

        if (analysis.Model is not { } model)
        {
            return new Failure(analysis.ModelProblem!, "-", "-");
        }

        using PairRuntime.Loaded loaded = new(analysis);
        (string legacy, string modern) = loaded.Observe(model);
        bool throughCall = counterexample.Old.Trace.Length > 0 || counterexample.New.Trace.Length > 0;
        return string.Equals(legacy, modern, StringComparison.Ordinal) && !throughCall ? new Failure(model.ToString(), legacy, modern) : null;
    }

    private static Failure? Precision(Case c, PairRuntime.Lowering lowering)
    {
        if (!PairGen.IsPreserving(c.Operator))
        {
            return null;
        }

        PairRuntime.Analysis analysis = PairRuntime.Analyse(c.Legacy, c.Modern, lowering);
        if (analysis.Verdict is not Divergent)
        {
            return null;
        }

        if (analysis.Model is not { } model)
        {
            return new Failure(analysis.ModelProblem!, "-", "-");
        }

        using PairRuntime.Loaded loaded = new(analysis);
        (string legacy, string modern) = loaded.Observe(model);
        return new Failure(model.ToString(), legacy, modern);
    }

    private sealed record Case(string Legacy, string Modern, MutationOperator Operator, PairInput[] Inputs)
    {
        public override string ToString() => $"{Operator}\n{Legacy}\n{Modern}";
    }

    /// <summary>One of the three rules, by its number in VERIFICATION-MODEL.md section 7.</summary>
    private sealed record Rule(int Number, Func<Case, PairRuntime.Lowering, Failure?> Check);

    /// <summary>
    /// What a rule saw go wrong on one input (or, for an undecodable model, why there is no input), and which rule under
    /// which lowering it <see cref="Broken"/>.
    /// </summary>
    private sealed record Failure(string Input, string LegacyObservable, string ModernObservable)
    {
        public string Broken { get; init; } = string.Empty;

        /// <summary>M0-012's acceptance criterion 4 (both methods, the operator, the input and both observables), after the rule and lowering broken.</summary>
        public string Describe(Case c) =>
            $"{Broken}\noperator: {c.Operator}\ninput: {Input}\nlegacy observable: {LegacyObservable}\nmodern observable: {ModernObservable}\nlegacy:\n{c.Legacy}modern:\n{c.Modern}";
    }
}
