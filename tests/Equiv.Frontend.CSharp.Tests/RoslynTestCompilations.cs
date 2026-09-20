using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests;

/// <summary>Builds a one-document C# compilation over an <c>AdhocWorkspace</c> for a source snippet.</summary>
internal static class RoslynTestCompilations
{
    public static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    public static Compilation Compile(string source, string assemblyName = "Snippet") => Compile(source, [], assemblyName);

    /// <summary>
    /// <paramref name="extraReferences"/> lets a snippet reference types compiled separately (e.g. via
    /// <see cref="ToReference"/>) instead of declaring them inline, so those types' own members never
    /// show up when a test walks <c>this</c> compilation's own symbols (M2-005: fake route attributes
    /// declared for a metadata-name-only lookup would otherwise add spurious constructors for
    /// <see cref="ProcedureEnumerator"/>/<c>CSharpFrontend.Analyze</c> to enumerate).
    /// </summary>
    public static Compilation Compile(string source, IEnumerable<MetadataReference> extraReferences, string assemblyName = "Snippet")
    {
        using AdhocWorkspace workspace = new();
        // Build the project from a ProjectInfo: Project.WithMetadataReferences returns a copy the workspace never sees.
        Project project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            assemblyName,
            assemblyName,
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [.. References, .. extraReferences]));
        Document document = workspace.AddDocument(project.Id, "Snippet.cs", SourceText.From(source));

        return document.Project.GetCompilationAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult()!;
    }

    public static MetadataReference ToReference(this Compilation compilation) => compilation.ToMetadataReference();
}
