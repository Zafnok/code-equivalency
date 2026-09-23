using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Core.Tests.Reporting;

public sealed class FileReportSinkTests
{
    [Fact]
    public void WriteSavesTheLogSoItRoundTripsThroughTheSdk()
    {
        string path = Path.GetTempFileName();
        try
        {
            SarifLog log = SarifReportWriter.Write([Fixtures.Result(new Equivalent(ProofMethod.Bounded))]);
            FileReportSink sink = new(path);

            sink.Write(log);

            SarifLog loaded = SarifLog.Load(path);

            // Not a whole-log ValueEquals: see SarifReportWriterTests.
            // WrittenLogDeclaresSarif210AndRoundTripsThroughTheSdk for why (default-value
            // omission on write, e.g. a rule's defaultConfiguration.level "warning").
            Assert.True(log.Runs[0].Results[0].ValueEquals(loaded.Runs[0].Results[0]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImplementsIReportSink()
    {
        Assert.IsType<IReportSink>(new FileReportSink(Path.GetTempFileName()), exactMatch: false);
    }

    [Fact]
    public void NullLogThrows()
    {
        FileReportSink sink = new(Path.GetTempFileName());
        Assert.Throws<ArgumentNullException>(() => sink.Write(null!));
    }
}
