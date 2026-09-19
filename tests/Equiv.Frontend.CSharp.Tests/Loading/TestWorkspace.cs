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

    public void AddCSharpProject(string name, string source, bool referenceCoreLibrary = true)
    {
        ProjectId projectId = ProjectId.CreateNewId(name);
        DocumentInfo document = DocumentInfo.Create(
            DocumentId.CreateNewId(projectId),
            $"{name}.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())));

        OnProjectAdded(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            documents: [document],
            metadataReferences: referenceCoreLibrary ? [CoreLibrary] : []));
    }

    public void Raise(WorkspaceDiagnosticKind kind, string message)
    {
        OnWorkspaceFailed(new WorkspaceDiagnostic(kind, message));
    }

    protected override void Dispose(bool finalize)
    {
        IsDisposed = true;
        base.Dispose(finalize);
    }
}
