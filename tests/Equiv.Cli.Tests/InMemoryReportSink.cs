using Equiv.Core;

using Microsoft.CodeAnalysis.Sarif;

namespace Equiv.Cli.Tests;

/// <summary>An <see cref="IReportSink"/> that keeps the last log written, for tests to inspect without touching disk.</summary>
internal sealed class InMemoryReportSink : IReportSink
{
    public SarifLog? Log { get; private set; }

    public void Write(SarifLog log) => Log = log;
}
