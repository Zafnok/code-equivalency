using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Equiv.Frontend.CSharp.Tests;

/// <summary>Builds a one-document C# compilation over an <c>AdhocWorkspace</c> for a source snippet.</summary>
internal static class RoslynTestCompilations
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    public static Compilation Compile(string source, string assemblyName = "Snippet")
    {
        using AdhocWorkspace workspace = new();
        Project project = workspace.AddProject(assemblyName, LanguageNames.CSharp)
            .WithMetadataReferences(References)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Document document = workspace.AddDocument(project.Id, "Snippet.cs", SourceText.From(source));

        return document.Project.GetCompilationAsync().GetAwaiter().GetResult()!;
    }
}
