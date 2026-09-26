using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket M4-009: a project emitted for replay with every referenced project and every referenced file outside a reference
/// pack next to it.
/// </summary>
public sealed class ProjectEmitterTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("project-emitter-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void AProjectGetsItsReferencedProjectsAndFilesButNothingFromAReferencePack()
    {
        string pack = Library("pack", "Ref", "[assembly: System.Runtime.CompilerServices.ReferenceAssembly] public class R { }");
        string facade = Library("pack", "Facade", "public class F { }");
        string package = Library("packages", "Package", "public class P { }");
        MetadataReference inMemory = MetadataReference.CreateFromImage(Image(Compile("public class I { }", "InMemory")));
        CSharpCompilation dependency = Compile("public class D { }", "Dependency", MetadataReference.CreateFromFile(package));
        CSharpCompilation other = Compile("public class O { D d; }", "Other", dependency.ToMetadataReference());
        CSharpCompilation project = Compile(
            "public class C { R r; F f; P p; D d; I i; O o; }",
            "Project",
            MetadataReference.CreateFromFile(pack),
            MetadataReference.CreateFromFile(facade),
            MetadataReference.CreateFromFile(package),
            inMemory,
            dependency.ToMetadataReference(),
            other.ToMetadataReference());
        string output = Path.Combine(root, "out");

        Assert.Null(ProjectEmitter.Emit(project, output));

        string[] files = [.. Directory.EnumerateFiles(output).Select(Path.GetFileName).OfType<string>()];
        Assert.Contains("Project.dll", files, StringComparer.Ordinal);
        Assert.Contains("Dependency.dll", files, StringComparer.Ordinal);
        Assert.Contains("Other.dll", files, StringComparer.Ordinal);
        Assert.Contains("Package.dll", files, StringComparer.Ordinal);
        Assert.Contains("System.Runtime.dll", files, StringComparer.Ordinal);
        Assert.DoesNotContain("Ref.dll", files, StringComparer.Ordinal);
        Assert.DoesNotContain("Facade.dll", files, StringComparer.Ordinal);
        Assert.DoesNotContain("InMemory.dll", files, StringComparer.Ordinal);
    }

    [Fact]
    public void AReferencedProjectThatDoesNotEmit_IsTheError()
    {
        CSharpCompilation dependency = Compile("public class D { int M() => missing; }", "Dependency");
        CSharpCompilation project = Compile("public class C { D d; }", "Project", dependency.ToMetadataReference());

        string? error = ProjectEmitter.Emit(project, Path.Combine(root, "out"));

        Assert.StartsWith("Dependency: ", error, StringComparison.Ordinal);
        Assert.Contains("CS0103", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "out", "Project.dll")));
    }

    private string Library(string folder, string name, string source)
    {
        string path = Path.Combine(root, folder, name + ".dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Image(Compile(source, name)));
        return path;
    }

    private static byte[] Image(CSharpCompilation compilation)
    {
        using MemoryStream image = new();
        EmitResult result = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));
        return image.ToArray();
    }
}
