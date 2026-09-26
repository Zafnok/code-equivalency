using System.Globalization;

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
/// folder: <c>EquivReplay&lt;n&gt;.exe</c> with an <c>app.config</c> on the legacy side, run on .NET Framework 4.8, and
/// <c>EquivReplay&lt;n&gt;.dll</c> with a <c>runtimeconfig.json</c> on the modern side, run on .NET 10. Its source is written
/// beside it, so a reproduced divergence is one the user can read and run.
/// </summary>
internal sealed class ReplayDriverFactory(
    IReadOnlyDictionary<ProcedureIdentity, ReplayTarget> legacy,
    IReadOnlyDictionary<ProcedureIdentity, ReplayTarget> modern) : IReplayDriverFactory
{
    private const string EmitFailed = "emit-failed";

    private readonly Dictionary<(Compilation, string), string?> emitted = [];

    private int drivers;

    public ReplayPlan Create(ProcedurePair pair, Counterexample counterexample, string directory)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(counterexample);

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
        (string? legacyDriver, string legacyProblem) = Driver(old, Path.Combine(directory, "legacy"), number, legacy: true);
        if (legacyDriver is null)
        {
            return ReplayPlan.NotConstructible(legacyProblem);
        }

        (string? modernDriver, string modernProblem) = Driver(@new, Path.Combine(directory, "modern"), number, legacy: false);
        return modernDriver is null
            ? ReplayPlan.NotConstructible(modernProblem)
            : ReplayPlan.Runnable(new ExecutionDrivers(legacyDriver, modernDriver), oldCase.Input, newCase.Input);
    }

    /// <summary>Emits <paramref name="target"/>'s project, once, and compiles a driver beside it; or returns why it could not.</summary>
    private (string? Driver, string Problem) Driver(ReplayTarget target, string sideDirectory, int number, bool legacy)
    {
        string side = legacy ? "legacy" : "modern";
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
        string path = Path.Combine(project, name + (legacy ? ".exe" : ".dll"));
        string source = DriverSource.Generate(target.Method, constructReceiver: true);
        File.WriteAllText(Path.ChangeExtension(path, ".cs"), source);
        CSharpCompilation driver = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(legacy ? LanguageVersion.CSharp7_3 : LanguageVersion.Latest))],
            [.. target.Compilation.References, target.Compilation.ToMetadataReference()],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release, deterministic: true));
        EmitResult result = driver.Emit(path);
        if (!result.Success)
        {
            return (null, $"the {side} driver does not compile: {string.Join("; ", result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error))}");
        }

        File.WriteAllText(legacy ? path + ".config" : Path.ChangeExtension(path, ".runtimeconfig.json"), legacy ? DriverFactory.AppConfig : DriverFactory.RuntimeConfig);
        return (path, string.Empty);
    }
}
