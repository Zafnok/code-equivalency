using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// A project the loader did not keep (ADR 0029 decision 1). <paramref name="IsCSharp"/> is false for a project in
/// another language, which is reported but does not make the run incomplete. <paramref name="Compilation"/> is the
/// skipped C# project's compilation when one exists, so its procedures can still be listed as unverified; it is
/// never lowered. <paramref name="Name"/> and <paramref name="AssemblyName"/> are empty for a workspace failure that
/// names no project.
/// </summary>
internal sealed record SkippedProject(
    string Name,
    string AssemblyName,
    bool IsCSharp,
    ImmutableArray<LoadDiagnostic> Diagnostics,
    Compilation? Compilation);
