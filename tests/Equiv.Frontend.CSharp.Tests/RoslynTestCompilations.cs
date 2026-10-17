using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
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
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true),
            metadataReferences: [.. References, .. extraReferences]));
        Document document = workspace.AddDocument(project.Id, "Snippet.cs", SourceText.From(source));

        return document.Project.GetCompilationAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult()!;
    }

    /// <summary>
    /// A one-file compilation whose file is at <paramref name="path"/>, with no errors: what the compiler supplies for a
    /// <c>[CallerFilePath]</c> parameter is that path (ticket P2-098).
    /// </summary>
    public static Compilation CompileAt(string source, string path)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText(SourceText.From(source, System.Text.Encoding.UTF8), path: path, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    public static MetadataReference ToReference(this Compilation compilation) => compilation.ToMetadataReference();

    /// <summary>
    /// <paramref name="source"/> compiled as <paramref name="assembly"/> to a file of its own in a new temporary directory, as
    /// a restored package is (ADR 0061; ticket P2-179), or as a reference assembly when <paramref name="referenceAssembly"/> is
    /// set. Returns the file's path.
    /// </summary>
    public static string EmitLibrary(string assembly, string source, bool referenceAssembly = false, IEnumerable<MetadataReference>? references = null)
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("equiv-P2-179-").FullName, assembly + ".dll");
        CSharpCompilation compilation = CSharpCompilation.Create(
            assembly,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [.. References, .. references ?? []],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using FileStream file = File.Create(path);
        EmitResult emitted = compilation.Emit(file, options: new EmitOptions(metadataOnly: referenceAssembly, includePrivateMembers: !referenceAssembly), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return path;
    }

    /// <summary>A reference to the file at <paramref name="path"/>, read into memory so the file stays free to delete.</summary>
    public static PortableExecutableReference FileReference(string path) => MetadataReference.CreateFromImage(File.ReadAllBytes(path), filePath: path);
}
