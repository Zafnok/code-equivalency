using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>Ticket P2-056 criterion 3: <c>--execute</c> needs Windows only when some side's runtime is .NET Framework.</summary>
public sealed class ExecutionEnvironmentTests
{
    private static readonly ProcedureIdentity DivergentIdentity = new("T::Divergent()");

    [Fact]
    public void CoreOnlyPairNeedsNoWindows()
    {
        FakeReplay replay = new("[\"Returned\",1]", "[\"Returned\",2]");
        ExecutionEnvironment offWindows = new(IsWindows: false, replay);
        IrProcedure body = IrText.Parse("proc \"T::Divergent()\" () entry B0 B0: ret");
        FakeFrontend frontend = new(
            "csharp",
            _ => true,
            new MatchResult([new ProcedurePair(DivergentIdentity, DivergentIdentity, body, body)], [], [], []),
            replay: replay,
            legacyRuntimes: [("App", "net8.0", "attribute"), ("Lib", "netstandard2.0", "unhosted")],
            modernRuntimes: [("App", "net10.0", "attribute")]);
        FakeBackend backend = new(new Dictionary<string, Verdict>(StringComparer.Ordinal)
        {
            [DivergentIdentity.Value] = new Divergent(new Counterexample(
                new IrInputs([]), new IrRun(new IrReturned(new IrBitVecValue(32, 1)), [], []), new IrRun(new IrReturned(new IrBitVecValue(32, 2)), [], []))),
        });
        using TempFile legacy = new();
        using TempFile modern = new();
        using StringWriter error = new();
        InMemoryReportSink sink = new();
        CompareOptions options = new(legacy.Path, modern.Path, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false, Execute: true)
        {
            Streams = new Streams(TextWriter.Null, error),
        };

        int exitCode = CompareCommand.Run(options, [frontend], backend, sink, NullRunLog.Instance, offWindows);

        Assert.Equal(ExitCodes.Divergent, exitCode);
        Assert.Equal(ExecutionEnvironment.Note + Environment.NewLine, error.ToString());
        Assert.Single(replay.Creates);
        Assert.Equal("reproduced", sink.Log!.Runs[0].Results.Single().GetProperty<string>("replay"));
    }
}
