using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// One project the bare loader opened (M3-029): its compilation when one was built, and the diagnostics that bear on
/// it, classified as <see cref="MsBuildSolutionLoader"/> classifies them. <see cref="IsSkipped"/> follows the same rule:
/// a project in another language, a workspace failure (for the bare loader: MSBuild it cannot evaluate exactly, or a
/// missing input) or an unresolved reference skips the project.
/// </summary>
internal sealed record BareProject(string Name, string Path, string AssemblyName, bool IsCSharp, Compilation? Compilation, ImmutableArray<LoadDiagnostic> Diagnostics)
{
    public bool IsSkipped =>
        !IsCSharp || Compilation is null || Diagnostics.Any(static d => d.Kind is LoadDiagnosticKind.WorkspaceFailure or LoadDiagnosticKind.UnresolvedReference);

    public SkippedProject ToSkipped() => new(Name, AssemblyName, IsCSharp, Diagnostics, Compilation);
}
