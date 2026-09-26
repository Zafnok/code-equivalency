using System.Text;

using Equiv.Core.Execution;

using Xunit;

namespace Equiv.Execute.Tests;

public sealed class ReportWriterTests
{
    /// <summary>
    /// Inputs and canonical outcomes are the drivers' own text, written as they came: a driver line the runner could not
    /// fully vet still reaches the report instead of aborting it. The report is indented for people to read.
    /// </summary>
    [Fact]
    public void DriverTextIsWrittenRawAndTheReportIsIndented()
    {
        ExecutionInput input = new(["raw-input"]);
        OverloadReport overload = new(
            "M()",
            1,
            1,
            [new OverloadReport.Witness(new ExecutionOutcome(input, "invariant", OutcomeKind.Returned, "raw-legacy"), new ExecutionOutcome(input, "invariant", OutcomeKind.Threw, "raw-modern"))],
            0,
            0,
            0,
            0,
            []);
        using MemoryStream stream = new();

        ReportWriter.Write(stream, new RuntimeDiffOptions("M(", 0, 1, "r.json"), ["invariant"], [overload]);

        string report = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("raw-input", report, StringComparison.Ordinal);
        Assert.Contains("\"value\": raw-legacy", report, StringComparison.Ordinal);
        Assert.Contains("\"value\": raw-modern", report, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"Threw\"", report, StringComparison.Ordinal);
        Assert.StartsWith("{" + Environment.NewLine + "  \"member\": \"M(\"", report.ReplaceLineEndings(), StringComparison.Ordinal);
    }
}
