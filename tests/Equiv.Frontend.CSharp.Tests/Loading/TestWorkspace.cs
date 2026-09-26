using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Equiv.Frontend.CSharp.Tests.Loading;

/// <summary>
/// An in-memory workspace (what <c>AdhocWorkspace</c> does, which is sealed) that can also raise
/// <c>WorkspaceFailed</c> and reports its own disposal.
/// </summary>
internal sealed class TestWorkspace() : Workspace(MefHostServices.DefaultHost, "Test")
{
    private static readonly MetadataReference CoreLibrary = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    public bool IsDisposed { get; private set; }

    /// <param name="raiseOnTextLoad">
    /// Raised when the document text is first read, which happens during <c>GetCompilationAsync</c>, not when the solution opens.
    /// </param>
    public void AddCSharpProject(string name, string source, bool referenceCoreLibrary = true, WorkspaceDiagnostic? raiseOnTextLoad = null, string? filePath = null)
    {
        TextAndVersion text = TextAndVersion.Create(SourceText.From(source), VersionStamp.Create());
        ProjectId projectId = ProjectId.CreateNewId(name);
        DocumentInfo document = DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            $"{name}.cs",
            loader: raiseOnTextLoad is null ? TextLoader.From(text) : new RaisingTextLoader(this, text, raiseOnTextLoad));

        OnProjectAdded(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            filePath: filePath,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            documents: [document],
            metadataReferences: referenceCoreLibrary ? [CoreLibrary] : []));
    }

    /// <summary>Adds a project exactly as given, for tests that need project references or metadata references of their own.</summary>
    public void AddProject(ProjectInfo project) => OnProjectAdded(project);

    public void Raise(WorkspaceDiagnosticKind kind, string message)
    {
        OnWorkspaceFailed(new WorkspaceDiagnostic(kind, message));
    }

    protected override void Dispose(bool finalize)
    {
        IsDisposed = true;
        base.Dispose(finalize);
    }

    private sealed class RaisingTextLoader(TestWorkspace workspace, TextAndVersion text, WorkspaceDiagnostic diagnostic) : TextLoader
    {
        public override Task<TextAndVersion> LoadTextAndVersionAsync(LoadTextOptions options, CancellationToken cancellationToken)
        {
            workspace.OnWorkspaceFailed(diagnostic);
            return Task.FromResult(text);
        }
    }
}
