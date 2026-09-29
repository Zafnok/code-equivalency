using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.TestSupport;
using Equiv.Verify.Z3;
using Equiv.Verify.Z3.Contracts;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P1-010 criteria 2 and 3 (ADR 0036 decision 2): callee contracts never hide a divergence that execution shows.
/// Each case is a (C, f, f') triple: f and f' are an M0-012 <see cref="PairGen"/> pair, method <c>M</c> of class
/// <c>Oracle</c>, and C is a caller added unchanged to both sides that calls <c>M</c> and returns a generated predicate
/// of its result (or, for a <c>void</c> <c>M</c>, of the field <c>F</c> it may write), or the value itself. Both sides
/// are lowered by the real frontend, C's pair goes to <see cref="Z3Backend.VerifyUnderContracts"/> with the pair of
/// <c>M</c>, and both Cs run on the CLR on <see cref="InputsPerCase"/> CsCheck inputs and on <c>M</c>'s own
/// counterexample when it has one.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class ContractSoundnessTests
{
    private const int InputsPerCase = 20;

    /// <summary>Triples per pull request and per nightly run (<c>EQUIV_DIFFERENTIAL_BUDGET=nightly</c>), and the seed.</summary>
    private static readonly (int PullRequest, int Nightly, string Seed) Budget = (40, 1_000, "000000000000");

    private static readonly string[] Relations = ["<", "<=", ">", ">=", "==", "!="];

    private static int Cases =>
        string.Equals(Environment.GetEnvironmentVariable("EQUIV_DIFFERENTIAL_BUDGET"), "nightly", StringComparison.OrdinalIgnoreCase) ? Budget.Nightly : Budget.PullRequest;

    private static string Seed => Environment.GetEnvironmentVariable("EQUIV_DIFFERENTIAL_SEED") is { Length: > 0 } seed ? seed : Budget.Seed;

    /// <summary>A predicate of the callee's outcome, or the outcome itself: the caller's return expression over <c>r</c>.</summary>
    private static Gen<string> Use =>
        Gen.Frequency(
            (4, Gen.Select(Gen.OneOfConst(Relations), Gen.Int[-3, 3], static (op, k) => string.Create(CultureInfo.InvariantCulture, $"r {op} {k} ? 1 : 0"))),
            (1, Gen.Const("unchecked((int)r)")));

    private static Gen<Triple> Triples =>
        Gen.Select(PairGen.Pair, Use, PairGen.Input.Array[InputsPerCase], static (pair, use, inputs) => new Triple(pair.LegacySource, pair.ModernSource, use, inputs));

    /// <summary>Criterion 2: whenever the two callers are seen to differ, the contract step did not make the caller Equivalent.</summary>
    [Fact]
    public void ContractNeverHidesAnObservedDivergence() =>
        Triples.Sample(
            static t => Hidden(t, new Z3Backend()) is null,
            seed: Seed,
            iter: Cases,
            print: static t => $"{t}\n{Hidden(t, new Z3Backend())}");

    /// <summary>
    /// Criterion 3: K together with the shared function (built here only) proves some generated caller Equivalent that
    /// execution shows to differ, which is why the backend gives each side's call its own outcome and heap.
    /// </summary>
    [Fact]
    public void SharedFunctionUnderContractWouldBeUnsound()
    {
        Z3Backend naive = new() { ContractEncoding = new SharedFunctionEncoding() };
        string? found = null;

        Triples.Sample(
            t =>
            {
                found ??= Hidden(t, naive) is { } input ? $"{t}\n{input}" : null;
                return true;
            },
            seed: Seed,
            iter: 200,
            threads: 1);

        Assert.NotNull(found);
        Assert.Same(FreshPerSideEncoding.Instance, new Z3Backend().ContractEncoding);
        Assert.True(new Z3Backend().ContractEncoding.FreshPerSide);
    }

    /// <summary>
    /// The first input on which the two callers differ when <paramref name="backend"/> proves the caller Equivalent under a
    /// contract, described with both observables; null when it does not, or when the callers agree on every input.
    /// </summary>
    private static string? Hidden(Triple triple, Z3Backend backend)
    {
        Side legacy = Side.Of(triple.WithCaller(triple.Legacy), "Legacy");
        Side modern = Side.Of(triple.WithCaller(triple.Modern), "Modern");
        Assert.Contains(legacy.Caller.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>(), c => string.Equals(c.Callee.Value, legacy.Callee.Identity.Value, StringComparison.Ordinal));
        VerificationOptions options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);
        if (backend.VerifyUnderContracts(legacy.Caller, modern.Caller, [new CalleePair(legacy.Callee.Identity.Value, legacy.Callee, modern.Callee)], options) is null)
        {
            return null;
        }

        PairInput? model = PairRuntime.Analyse(triple.Legacy, triple.Modern).Model;
        AssemblyLoadContext context = new("contract-soundness", isCollectible: true);
        try
        {
            Type legacyType = legacy.Load(context);
            Type modernType = modern.Load(context);
            foreach (PairInput input in model is null ? triple.Inputs : [model, .. triple.Inputs])
            {
                string left = Observe(legacyType, input);
                string right = Observe(modernType, input);
                if (!string.Equals(left, right, StringComparison.Ordinal))
                {
                    return $"input: {input}\nlegacy observable: {left}\nmodern observable: {right}";
                }
            }

            return null;
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>The observables of one run of <c>C</c>: its return value or exception type, then <c>F</c> and <c>u</c> afterwards.</summary>
    private static string Observe(Type oracle, PairInput input)
    {
        FieldInfo field = oracle.GetField("F")!;
        field.SetValue(null, input.F);
        int[]? u = input.U is { } elements ? [.. elements] : null;
        string outcome;
        try
        {
            object? result = oracle.GetMethod("C")!.Invoke(null, [input.A, input.B, input.C, input.D, input.E, input.SIsNull ? null : "s", u]);
            outcome = $"return {Convert.ToString(result, CultureInfo.InvariantCulture)}";
        }
        catch (TargetInvocationException exception)
        {
            outcome = $"throw {exception.InnerException!.GetType().FullName}";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{outcome} F={(int)field.GetValue(null)!} u={(u is null ? "null" : $"[{string.Join(',', u)}]")}");
    }

    [GeneratedRegex(@"public static (?<type>\w+) M\(", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReturnType { get; }

    /// <summary>A PairGen pair, what the caller returns about the callee's outcome <c>r</c>, and the inputs.</summary>
    private sealed record Triple(string Legacy, string Modern, string Use, PairInput[] Inputs)
    {
        /// <summary>
        /// <paramref name="source"/> with the caller added to its class: it calls <c>M</c> with its own arguments and returns
        /// <see cref="Use"/> of the result, or of <c>F</c> when <c>M</c> returns nothing (a bool result is first read as 0 or 1).
        /// </summary>
        public string WithCaller(string source)
        {
            string type = ReturnType.Match(source).Groups["type"].Value;
            string outcome = type switch
            {
                "void" => "M(a, b, c, d, e, s, u); int r = F;",
                "bool" => "int r = M(a, b, c, d, e, s, u) ? 1 : 0;",
                _ => $"{type} r = M(a, b, c, d, e, s, u);",
            };
            string caller = $"\n    public static int C(int a, int b, long c, long d, bool e, string s, int[] u)\n    {{\n        {outcome}\n        return {Use};\n    }}\n";
            int end = source.LastIndexOf('}');
            return source[..end] + caller + source[end..];
        }

        public override string ToString() => $"legacy:\n{WithCaller(Legacy)}modern:\n{WithCaller(Modern)}";
    }

    /// <summary>One side of a triple: its lowered caller and callee, and its image.</summary>
    private sealed record Side(IrProcedure Caller, IrProcedure Callee, byte[] Image)
    {
        public static Side Of(string source, string assemblyName)
        {
            CSharpCompilation compilation = PairRuntime.Compile(source, assemblyName);
            using MemoryStream image = new();
            EmitResult emitted = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(emitted.Success, string.Join('\n', emitted.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
            INamedTypeSymbol oracle = compilation.GetTypeByMetadataName("Oracle")!;
            return new Side(Lower("C"), Lower("M"), image.ToArray());

            IrProcedure Lower(string name) => IrLowerer.Lower(oracle.GetMembers(name).OfType<IMethodSymbol>().Single(), compilation, RenameMap.Empty, []);
        }

        public Type Load(AssemblyLoadContext context)
        {
            using MemoryStream image = new(Image);
            return context.LoadFromStream(image).GetType("Oracle")!;
        }
    }

    /// <summary>ADR 0036's rejected encoding: the shared call functions, with K asserted on top.</summary>
    private sealed class SharedFunctionEncoding : ICalleeContractEncoding
    {
        public bool FreshPerSide => false;
    }
}
