using Microsoft.CodeAnalysis.Sarif;

namespace Equiv.Core;

/// <summary>
/// Persists a finished SARIF log (ARCHITECTURE.md). Ticket M1-004 ships the MVP implementation,
/// <see cref="Equiv.Core.Reporting.FileReportSink"/>; the extension-points table names a future
/// upload sink (GitHub Code Scanning / SonarQube) that this interface is deliberately narrow
/// enough to also cover.
/// </summary>
public interface IReportSink
{
    void Write(SarifLog log);
}
