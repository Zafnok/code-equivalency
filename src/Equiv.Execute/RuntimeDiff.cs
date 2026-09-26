using System.Globalization;

using Equiv.Core.Execution;
using Equiv.Execute.Inputs;

namespace Equiv.Execute;

/// <summary>
/// <c>runtime-diff</c> (ADR 0035, ticket M3-032): runs every overload matching <c>--member</c> on .NET Framework 4.8 and
/// on .NET 10 under a fixed culture set, and writes a report of the cases whose canonical outcomes differ. Exits
/// <see cref="NoDivergence"/>, <see cref="Divergence"/>, or <see cref="UsageError"/>; a non-Windows OS is a usage error.
/// </summary>
public sealed class RuntimeDiff(IExecutionDriverFactory factory, IDriverHost host, TextWriter output, TextWriter error)
{
    public const int NoDivergence = 0;

    public const int Divergence = 1;

    public const int UsageError = 3;

    public const string NeedsWindows = "runtime-diff needs Windows and .NET Framework 4.8 (ADR 0035)";

    /// <summary>The invariant culture, then en-US, tr-TR, de-DE and ja-JP.</summary>
    public static readonly IReadOnlyList<string> Cultures = ["invariant", "en-US", "tr-TR", "de-DE", "ja-JP"];

    internal static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Runs the tool; <paramref name="workDirectory"/> receives one folder of drivers per overload.</summary>
    public int Run(IReadOnlyList<string> args, bool isWindows, string workDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!isWindows)
        {
            error.WriteLine(NeedsWindows);
            return UsageError;
        }

        if (RuntimeDiffOptions.Parse(args, out string problem) is not { } options)
        {
            error.WriteLine(problem);
            error.WriteLine(RuntimeDiffOptions.Usage);
            return UsageError;
        }

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
            OverloadReport overload = Overload(signature, options, runner, Path.Combine(workDirectory, index.ToString(CultureInfo.InvariantCulture)));
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

    private OverloadReport Overload(ExecutionSignature signature, RuntimeDiffOptions options, DriverRunner runner, string directory)
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
