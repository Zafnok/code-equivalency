using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.TestSupport;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Compiles, runs and verifies a <see cref="PairGen"/> pair (ticket M0-012). Each side is its own compilation of the
/// class <c>Oracle</c>, lowered by the real frontend and emitted, and the two lowered methods go to the real
/// <see cref="Z3Backend"/> with the default bound and timeout. Execution is in-process, as in the lowering oracle: both
/// images load into one collectible <see cref="AssemblyLoadContext"/>. <see cref="Analyse"/> memoises by the two
/// sources, so the three rules of <see cref="DifferentialSoundnessTests"/> verify each pair once.
/// </summary>
internal static class PairRuntime
{
    private const string FieldMap = "field.Oracle.F";

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    /// <summary>The key a static field's map is read at: element 0 of its declaring type's sort.</summary>
    private static readonly IrSortValue Token = new("Oracle", 0);

    private static readonly ConcurrentDictionary<(string Legacy, string Modern), Lazy<Analysis>> Analyses = new();

    public static CSharpCompilation Compile(string source, string assemblyName) => CSharpCompilation.Create(
        assemblyName,
        [CSharpSyntaxTree.ParseText(source, path: $"{assemblyName}.cs", cancellationToken: TestContext.Current.CancellationToken)],
        References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    public static Analysis Analyse(string legacy, string modern) =>
        Analyses.GetOrAdd((legacy, modern), static key => new Lazy<Analysis>(() => Verify(key.Legacy, key.Modern))).Value;

    private static Analysis Verify(string legacy, string modern)
    {
        (IrProcedure old, byte[] oldImage) = Lower(legacy, "Legacy");
        (IrProcedure @new, byte[] newImage) = Lower(modern, "Modern");
        Verdict verdict = new Z3Backend().Verify(old, @new, new VerificationOptions(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []));
        (PairInput? model, string? problem) = verdict is Divergent divergent ? Decode(old, @new, divergent.Counterexample.Inputs) : (null, null);
        return new Analysis(verdict, model, problem, oldImage, newImage) { Old = old, New = @new };
    }

    private static (IrProcedure Procedure, byte[] Image) Lower(string source, string assemblyName)
    {
        CSharpCompilation compilation = Compile(source, assemblyName);
        using MemoryStream image = new();
        EmitResult emitted = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join('\n', emitted.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
        IMethodSymbol method = compilation.GetTypeByMetadataName("Oracle")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        return (IrLowerer.Lower(method, compilation, RenameMap.Empty, []), image.ToArray());
    }

    /// <summary>
    /// A Divergent model as C# arguments. The model's inputs are the shared inputs of the product encoding: the legacy
    /// side's parameters, then the modern side's that the legacy side lacks (ADR 0021), which for two methods with one
    /// signature pair by name. An array longer than three is replayed as three elements long, because no generated
    /// method reads past index 2 or reads the length. A value the model cannot give a C# run is a problem, not an input.
    /// </summary>
    private static (PairInput? Model, string? Problem) Decode(IrProcedure old, IrProcedure @new, IrInputs inputs)
    {
        ImmutableArray<string> names = [.. old.Parameters.Select(static p => p.Var.Name), .. @new.Parameters.Select(static p => p.Var.Name).Where(n => !old.Parameters.Any(p => string.Equals(p.Var.Name, n, StringComparison.Ordinal)))];
        if (names.Length != inputs.Arguments.Length)
        {
            return (null, $"the model has {inputs.Arguments.Length} inputs for the {names.Length} shared parameters {string.Join(", ", names)}");
        }

        Dictionary<string, IrValue> model = names.Zip(inputs.Arguments).ToDictionary(static e => e.First, static e => e.Second, StringComparer.Ordinal);
        int field = model.TryGetValue(FieldMap, out IrValue? map) ? (int)Bits(((IrMapValue)map).Read(Token)) : 0;
        ImmutableArray<int>? u = null;
        if (model.TryGetValue("u", out IrValue? array) && !IsNull(model, array))
        {
            long length = model.TryGetValue("length.int__", out IrValue? lengths) ? Bits(((IrMapValue)lengths).Read(array)) : 0;
            if (length < 0)
            {
                return (null, string.Create(CultureInfo.InvariantCulture, $"the model gives u the length {length}"));
            }

            IrMapValue? elements = model.TryGetValue("array.int__", out IrValue? slices) ? (IrMapValue)((IrMapValue)slices).Read(array) : null;
            u = [.. Enumerable.Range(0, (int)Math.Min(length, 3)).Select(i => elements is null ? 0 : (int)Bits(elements.Read(IrBitVecValue.FromSigned(32, i))))];
        }

        return (new PairInput(
            (int)Bits(model.GetValueOrDefault("a")),
            (int)Bits(model.GetValueOrDefault("b")),
            Bits(model.GetValueOrDefault("c")),
            Bits(model.GetValueOrDefault("d")),
            model.GetValueOrDefault("e") is IrBoolValue { Value: true },
            model.TryGetValue("s", out IrValue? s) && IsNull(model, s),
            field,
            u), null);
    }

    private static long Bits(IrValue? value) => value is IrBitVecValue bits ? bits.TwosComplement : 0;

    /// <summary>Whether the model's <c>null.&lt;Sort&gt;</c> input holds at <paramref name="reference"/>.</summary>
    private static bool IsNull(Dictionary<string, IrValue> model, IrValue reference) =>
        model.Any(e => e.Key.StartsWith("null.", StringComparison.Ordinal) && e.Value is IrMapValue nulls && nulls.MapType.Key == reference.Type
            && nulls.Read(reference) is IrBoolValue { Value: true });

    /// <summary>A verified pair: the verdict, its model as C# arguments (or why it has none), both emitted images and both lowered bodies.</summary>
    internal sealed record Analysis(Verdict Verdict, PairInput? Model, string? ModelProblem, byte[] Legacy, byte[] Modern)
    {
        public required IrProcedure Old { get; init; }

        public required IrProcedure New { get; init; }
    }

    /// <summary>Both sides of an <see cref="Analysis"/>, loaded to run; unloads on dispose.</summary>
    internal sealed class Loaded : IDisposable
    {
        private readonly AssemblyLoadContext context = new("differential-soundness", isCollectible: true);
        private readonly Type legacy;
        private readonly Type modern;

        public Loaded(Analysis analysis)
        {
            using MemoryStream legacyImage = new(analysis.Legacy);
            using MemoryStream modernImage = new(analysis.Modern);
            legacy = context.LoadFromStream(legacyImage).GetType("Oracle")!;
            modern = context.LoadFromStream(modernImage).GetType("Oracle")!;
        }

        public (string Legacy, string Modern) Observe(PairInput input) => (Observe(legacy, input), Observe(modern, input));

        public void Dispose() => context.Unload();

        /// <summary>The observables of one run: the return value or exception type, then <c>F</c> and <c>u</c> afterwards.</summary>
        private static string Observe(Type oracle, PairInput input)
        {
            FieldInfo field = oracle.GetField("F")!;
            field.SetValue(null, input.F);
            int[]? u = input.U is { } elements ? [.. elements] : null;
            string outcome;
            try
            {
                object? result = oracle.GetMethod("M")!.Invoke(null, [input.A, input.B, input.C, input.D, input.E, input.SIsNull ? null : "s", u]);
                outcome = $"return {Convert.ToString(result, CultureInfo.InvariantCulture)}";
            }
            catch (TargetInvocationException exception)
            {
                outcome = $"throw {exception.InnerException!.GetType().FullName}";
            }

            return string.Create(CultureInfo.InvariantCulture, $"{outcome} F={(int)field.GetValue(null)!} u={(u is null ? "null" : $"[{string.Join(',', u)}]")}");
        }
    }
}
