using Microsoft.CodeAnalysis.Sarif;

namespace Equiv.Core.Reporting;

/// <summary>The MVP <see cref="IReportSink"/> (ARCHITECTURE.md's extension-points table): writes to a file.</summary>
public sealed class FileReportSink(string path) : IReportSink
{
    public void Write(SarifLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        log.Save(path);
    }
}
