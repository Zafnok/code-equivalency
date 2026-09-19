namespace Equiv.Frontend.CSharp.Loading;

/// <param name="Kind">What produced the diagnostic, and so whether it aborts the load.</param>
/// <param name="Id">Compiler diagnostic id (<c>CS0246</c>); empty for workspace and solution diagnostics.</param>
/// <param name="Project">Project name; empty when the diagnostic is not tied to one project.</param>
/// <param name="Message">Human-readable text, invariant culture.</param>
internal sealed record LoadDiagnostic(LoadDiagnosticKind Kind, string Id, string Project, string Message);
