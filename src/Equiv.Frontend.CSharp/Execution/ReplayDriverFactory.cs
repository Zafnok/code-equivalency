using System.Globalization;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// <see cref="IReplayDriverFactory"/> over the projects one <see cref="CSharpFrontend.Analyze"/> loaded (ADR 0035 decision
/// 2; ticket M4-009). Each project a replay needs is emitted once, into <c>&lt;directory&gt;/&lt;side&gt;/&lt;assembly&gt;</c>
/// (<see cref="ProjectEmitter"/>), and each replay's driver is compiled against that project's own references into the same
/// folder, for that project's detected runtime (<see cref="ReplayTarget.Runtime"/>; ADR 0040 decision 3, ticket P2-056):
/// <c>EquivReplay&lt;n&gt;.exe</c> with an <c>app.config</c> on .NET Framework, <c>EquivReplay&lt;n&gt;.dll</c> with a
/// <c>runtimeconfig.json</c> on .NET (<see cref="DriverRuntime"/>). A same-runtime pair runs both sides on that one runtime.
/// A runtime <paramref name="hosts"/> does not find installed makes the plan not constructible, and none is used in its
/// place. Its source is written beside it, so a reproduced divergence is one the user can read and run. <see cref="Plan"/> builds the same two drivers
/// for an Unknown pair to be tested on generated inputs (ticket P1-008). <see cref="Probe"/> builds them for one case an
/// agent supplies itself, with no model in play (ADR 0035, ADR 0036; ticket M5-002).
/// </summary>
internal sealed class ReplayDriverFactory(
    IReadOnlyDictionary<ProcedureIdentity, ReplayTarget> legacy,
    IReadOnlyDictionary<ProcedureIdentity, ReplayTarget> modern,
    Func<TargetRuntime, DriverRuntime?> hosts) : IReplayDriverFactory
{
    private const string EmitFailed = "emit-failed";
    private const string ModernSide = "modern";

    private readonly Dictionary<(Compilation, string), string?> emitted = [];

    private int drivers;

    public ReplayPlan Create(ProcedurePair pair, Counterexample counterexample, string directory)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(counterexample);

        // A driver observes only the outcome; by-ref parameters and heap maps are never constructible, so a divergence
        // whose runs end alike is in the call trace.
        if (counterexample.Old.Outcome == counterexample.New.Outcome)
        {
            return ReplayPlan.NotConstructible("the divergence is in the call trace, which replay does not observe");
        }

        ReplayTarget old = legacy[pair.Old];
        ReplayTarget @new = modern[pair.New];
        if (ReplayArguments.Bind(pair.OldBody!, pair.NewBody!, counterexample.Inputs) is not var (oldValues, newValues))
        {
            return ReplayPlan.NotConstructible("the model is not over the pair's parameters");
        }

        Dictionary<string, IrValue> nullness = ReplayArguments.Nullness(oldValues, newValues);
        ReplayArguments.SideCase oldCase = ReplayArguments.Case(old.Method, pair.OldBody!, oldValues, nullness);
        ReplayArguments.SideCase newCase = ReplayArguments.Case(@new.Method, pair.NewBody!, newValues, nullness);
        if (oldCase.Input is null || newCase.Input is null)
        {
            return ReplayPlan.NotConstructible(oldCase.Input is null ? $"legacy: {oldCase.Reason}" : $"modern: {newCase.Reason}");
        }

        int number = ++drivers;
        (ExecutionDrivers? pairDrivers, string problem) = SideDrivers(old, @new, directory, number);
        return pairDrivers is null
            ? ReplayPlan.NotConstructible(problem)
            : ReplayPlan.Runnable(pairDrivers, oldCase.Input, newCase.Input) with { AlikeReason = AlikeReason(counterexample) };
    }

    /// <summary>
    /// Why equal real outcomes would not refute <paramref name="counterexample"/> (ticket P2-037): once its call traces
    /// differ, a call's result or <c>threw</c> answer after the split is the solver's free choice (ADR 0026, "Why"), so the
    /// outcomes may differ only by it. Git Extensions' <c>SetSsh</c> threw on the legacy side only through such an answer.
    /// Traces are compared by raw identity, so a callee the call-identity map renames counts as a split: that errs towards
    /// not constructible.
    /// </summary>
    private static string? AlikeReason(Counterexample counterexample) =>
        counterexample.Old.Trace.SequenceEqual(counterexample.New.Trace)
            ? null
            : "the call traces differ, and the model's outcomes rest on call answers chosen after they split, which replay does not observe";

    public TestingPlan Plan(ProcedurePair pair, Counterexample? candidate, string directory)
    {
        ArgumentNullException.ThrowIfNull(pair);

        ReplayTarget old = legacy[pair.Old];
        ReplayTarget @new = modern[pair.New];
        if (Obstacle(old.Method, @new.Method) is { } obstacle)
        {
            return TestingPlan.NotConstructible(obstacle);
        }

        int number = ++drivers;
        (ExecutionDrivers? pairDrivers, string problem) = SideDrivers(old, @new, directory, number);
        return pairDrivers is null
            ? TestingPlan.NotConstructible(problem)
            : TestingPlan.Runnable(
                pairDrivers,
                [.. @new.Method.Parameters.Zip(old.Method.Parameters, static (n, l) => DriverFactory.Parameter(n.Type, RefKind.None, l.Type))],
                Seeds(pair, old, candidate));
    }

    public ReplayPlan Probe(ProcedurePair pair, IReadOnlyList<JsonElement> arguments, string directory)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(arguments);

        ReplayTarget old = legacy[pair.Old];
        ReplayTarget @new = modern[pair.New];
        ProbeArguments.SideCase oldCase = ProbeArguments.Case(old.Method, arguments);
        ProbeArguments.SideCase newCase = ProbeArguments.Case(@new.Method, arguments);
        if (oldCase.Input is null || newCase.Input is null)
        {
            return ReplayPlan.NotConstructible(oldCase.Input is null ? $"legacy: {oldCase.Reason}" : $"modern: {newCase.Reason}");
        }

        int number = ++drivers;
        (ExecutionDrivers? pairDrivers, string problem) = SideDrivers(old, @new, directory, number);
        return pairDrivers is null
            ? ReplayPlan.NotConstructible(problem)
            : ReplayPlan.Runnable(pairDrivers, oldCase.Input, newCase.Input);
    }

    /// <summary>
    /// Why generated inputs cannot be given to both methods alike: either cannot be called, they take parameters of
    /// different kinds, position by position, or a kind the M3-032 generators cannot build.
    /// </summary>
    private static string? Obstacle(IMethodSymbol old, IMethodSymbol @new)
    {
        if (ReplayArguments.CallObstacle(old) is { } oldObstacle)
        {
            return $"legacy: {oldObstacle}";
        }

        if (ReplayArguments.CallObstacle(@new) is { } newObstacle)
        {
            return $"modern: {newObstacle}";
        }

        bool sameKinds = old.Parameters.Select(static p => DriverFactory.Classify(p.Type)).SequenceEqual(@new.Parameters.Select(static p => DriverFactory.Classify(p.Type)));
        IParameterSymbol? unsupported = old.Parameters.FirstOrDefault(static p => DriverFactory.Classify(p.Type) == ExecutionTypeKind.Unsupported);
        return (sameKinds, unsupported) switch
        {
            (false, _) => $"the two sides' parameters differ: {Types(old)} and {Types(@new)}",
            (true, { } parameter) => $"no input can be built for {parameter.Type.ToDisplayString()}",
            _ => null,
        };
    }

    private static string Types(IMethodSymbol method) => string.Join(", ", method.Parameters.Select(static p => p.Type.ToDisplayString()));

    /// <summary>The legacy case <paramref name="candidate"/>'s inputs give, when the model can be built as arguments at all.</summary>
    private static List<ExecutionInput> Seeds(ProcedurePair pair, ReplayTarget old, Counterexample? candidate)
    {
        if (candidate is null || ReplayArguments.Bind(pair.OldBody!, pair.NewBody!, candidate.Inputs) is not var (oldValues, newValues))
        {
            return [];
        }

        ReplayArguments.SideCase seed = ReplayArguments.Case(old.Method, pair.OldBody!, oldValues, ReplayArguments.Nullness(oldValues, newValues));
        return seed.Input is { } input ? [input] : [];
    }

    /// <summary>Compiles a driver for each side of the pair, in that order; or returns why one could not be.</summary>
    private (ExecutionDrivers? Drivers, string Problem) SideDrivers(ReplayTarget old, ReplayTarget @new, string directory, int number)
    {
        (string? legacyDriver, string legacyProblem) = Driver(old, Path.Combine(directory, "legacy"), number, legacy: true);
        if (legacyDriver is null)
        {
            return (null, legacyProblem);
        }

        (string? modernDriver, string modernProblem) = Driver(@new, Path.Combine(directory, ModernSide), number, legacy: false);
        return modernDriver is null ? (null, modernProblem) : (new ExecutionDrivers(legacyDriver, modernDriver), string.Empty);
    }

    /// <summary>Emits <paramref name="target"/>'s project, once, and compiles a driver beside it; or returns why it could not.</summary>
    private (string? Driver, string Problem) Driver(ReplayTarget target, string sideDirectory, int number, bool legacy)
    {
        string side = legacy ? "legacy" : ModernSide;
        if (Host(target) is not { } host)
        {
            return (null, target.Runtime is { } runtime ? DriverFactory.NotInstalled(runtime) : $"the {side} project {target.Compilation.AssemblyName} has no detected runtime");
        }

        string project = Path.Combine(sideDirectory, target.Compilation.AssemblyName!);
        if (!emitted.TryGetValue((target.Compilation, sideDirectory), out string? failure))
        {
            failure = ProjectEmitter.Emit(target.Compilation, project);
            emitted[(target.Compilation, sideDirectory)] = failure;
        }

        if (failure is not null)
        {
            return (null, $"{EmitFailed}: {side} project {failure}");
        }

        string name = "EquivReplay" + number.ToString(CultureInfo.InvariantCulture);
        string path = Path.Combine(project, name + host.Extension);
        string source = DriverSource.Generate(target.Method, constructReceiver: true);
        File.WriteAllText(Path.ChangeExtension(path, ".cs"), source);
        CSharpCompilation driver = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(host.Language))],
            [.. target.Compilation.References, target.Compilation.ToMetadataReference()],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release, deterministic: true));
        EmitResult result = driver.Emit(path);
        if (!result.Success)
        {
            return (null, $"the {side} driver does not compile: {string.Join("; ", result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error))}");
        }

        host.WriteConfig(path, desktop: false);
        return (path, string.Empty);
    }

    /// <summary>The installed runtime <paramref name="target"/>'s project runs on, or null when it has none or it is not installed.</summary>
    private DriverRuntime? Host(ReplayTarget target) => target.Runtime is { } runtime ? hosts(runtime) : null;
}
