namespace Equiv.Core.Configuration;

/// <summary>One validation finding. <see cref="Id"/> is one of <see cref="EquivConfigDiagnosticIds"/>; <see cref="Path"/> is a JSON pointer to the offending value.</summary>
public sealed record EquivConfigDiagnostic(string Id, string Path, string Message);
