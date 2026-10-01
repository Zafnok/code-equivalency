using System.Globalization;

using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Execute.Inputs;

namespace Equiv.Execute;

/// <summary>
/// <c>runtime-diff</c> (ADR 0035, ADR 0040 decision 3; tickets M3-032, P2-056): runs every overload matching <c>--member</c>
/// on the <c>--from</c> runtime and on the <c>--to</c> runtime (.NET Framework 4.8 and .NET 10 by default), each driver built
/// by <paramref name="factories"/> for that pair, under a fixed culture set, and writes a report of the cases whose
/// canonical outcomes differ. Exits <see cref="NoDivergence"/>, <see cref="Divergence"/>, or <see cref="UsageError"/>; a
/// .NET Framework runtime off Windows is a usage error (<see cref="WindowsRequirement"/>).
/// </summary>
public sealed class RuntimeDiff(Func<TargetRuntime, TargetRuntime, IExecutionDriverFactory> factories, IDriverHost host, TextWriter output, TextWriter error)
{
    public const int NoDivergence = 0;

    public const int Divergence = 1;

    public const int UsageError = 3;

    /// <summary>The invariant culture, then en-US, tr-TR, de-DE and ja-JP.</summary>
    public static IReadOnlyList<string> Cultures => ["invariant", "en-US", "tr-TR", "de-DE", "ja-JP"];

    internal static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Runs the tool; <paramref name="workDirectory"/> receives one folder of drivers per overload.</summary>
    public int Run(IReadOnlyList<string> args, bool isWindows, string workDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (RuntimeDiffOptions.Parse(args, out string problem) is not { } options)
        {
            error.WriteLine(problem);
            error.WriteLine(RuntimeDiffOptions.Usage);
            return UsageError;
        }

        if (WindowsRequirement.Refusal(isWindows, [("--from", options.From.ToString()), ("--to", options.To.ToString())]) is { } refusal)
        {
            error.WriteLine("runtime-diff: " + refusal);
            return UsageError;
        }

        IExecutionDriverFactory factory = factories(options.From, options.To);
        IReadOnlyList<ExecutionSignature> signatures = factory.Resolve(options.Member);
        if (signatures.Count == 0)
        {
            error.WriteLine($"no public member on both runtimes matches {options.Member}");
            return UsageError;
        }

        DriverRunner runner = new(host, CaseTimeout);
        List<OverloadReport> overloads = [];
        foreach ((ExecutionSignature signature, int index) in signatures.Select(static (s, i) => (s, i)))
        {
            OverloadReport overload = Overload(factory, signature, options, runner, Path.Combine(workDirectory, index.ToString(CultureInfo.InvariantCulture)));
            output.WriteLine(Summary(overload));
            overloads.Add(overload);
        }

        using (FileStream report = File.Create(options.Out))
        {
            ReportWriter.Write(report, options, Cultures, overloads);
        }

        output.WriteLine($"report: {options.Out}");
        return overloads.Exists(static o => o.Divergent > 0) ? Divergence : NoDivergence;
    }

    private static OverloadReport Overload(IExecutionDriverFactory factory, ExecutionSignature signature, RuntimeDiffOptions options, DriverRunner runner, string directory)
    {
        if (signature.NotConstructible.Count > 0)
        {
            return OverloadReport.NotRun(signature.Member.Value, signature.NotConstructible);
        }

        ExecutionRequest request = new(signature.Member, InputGenerator.Generate(signature.Parameters, options.Seed, options.Cases), Cultures);
        ExecutionDrivers drivers;
        try
        {
            drivers = factory.Create(request, directory);
        }
        catch (InvalidOperationException exception)
        {
            return OverloadReport.NotRun(signature.Member.Value, [exception.Message]);
        }

        return RuntimeComparison.Compare(signature.Member.Value, runner.Run(drivers, request));
    }

    private static string Summary(OverloadReport overload) => overload.NotConstructible.Count > 0
        ? $"{overload.Member}: not constructible ({string.Join("; ", overload.NotConstructible)})"
        : string.Create(CultureInfo.InvariantCulture, $"{overload.Member}: {overload.CasesRun} cases, {overload.Divergent} divergent, nondeterministic legacy {overload.LegacyNondeterministic} modern {overload.ModernNondeterministic} both {overload.BothNondeterministic}, {overload.NotComparable} not comparable");
}
