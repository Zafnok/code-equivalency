using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket M4-009: a project emitted for replay with every referenced project and every referenced file outside a reference
/// pack next to it. Ticket P2-052: each emitted project names the replay driver as its friend.
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

        Assert.Null(ProjectEmitter.Emit(project, output, new DriverKey()).Error);

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

        string? error = ProjectEmitter.Emit(project, Path.Combine(root, "out"), new DriverKey()).Error;

        Assert.StartsWith("Dependency: ", error, StringComparison.Ordinal);
        Assert.Contains("CS0103", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "out", "Project.dll")));
    }

    /// <summary>Ticket P2-052 criterion 1, for a project that is not strong-named: the friend is named, with no key.</summary>
    [Fact]
    public void EmitsInternalsVisibleToTheDriver()
    {
        CSharpCompilation dependency = CSharpCompilation.Create("Dependency", [], Runtime, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        CSharpCompilation project = Compile(InternalSource, "Project", dependency.ToMetadataReference());
        string output = Path.Combine(root, "out");

        (Compilation granted, string? error) = ProjectEmitter.Emit(project, output, new DriverKey());

        Assert.Null(error);
        Assert.Single(project.SyntaxTrees);
        Assert.Equal(2, granted.SyntaxTrees.Count());
        Assert.All(granted.SyntaxTrees, tree => Assert.Equal(project.SyntaxTrees[0].Options, tree.Options));
        Assert.Equal(["EquivReplay"], Friends(Path.Combine(output, "Project.dll")));
        Assert.Equal(["EquivReplay"], Friends(Path.Combine(output, "Dependency.dll")));
        Assert.Empty(Errors(Driver("EquivReplay", granted.ToMetadataReference())));
        Assert.Empty(Errors(Driver("EquivReplay", Emitted(Path.Combine(output, "Project.dll")))));
        Assert.Equal(["CS0122"], Errors(Driver("Other", Emitted(Path.Combine(output, "Project.dll")))));
        Assert.Equal(["CS0122"], Errors(Driver("EquivReplay", project.ToMetadataReference())));
    }

    /// <summary>
    /// Ticket P2-052 criterion 1, for a strong-named project: it may name a friend only with a public key (CS1726), so the
    /// attribute carries the run's key and only a driver signed with it is the friend.
    /// </summary>
    [Fact]
    public void StrongNamedProject_GrantsTheSignedDriver()
    {
        CSharpCompilation unsigned = Compile(InternalSource, "Project");
        CSharpCompilation project = unsigned.WithOptions(Signed(unsigned.Options, new DriverKey()));
        DriverKey key = new();
        string output = Path.Combine(root, "out");

        (Compilation granted, string? error) = ProjectEmitter.Emit(project, output, key);

        Assert.Null(error);
        Assert.True(granted.Assembly.Identity.IsStrongName);
        Assert.Equal([$"EquivReplay, PublicKey={Convert.ToHexString(key.PublicKey.AsSpan())}"], Friends(Path.Combine(output, "Project.dll")));
        CSharpCompilation driver = Driver("EquivReplay", Emitted(Path.Combine(output, "Project.dll")));
        CSharpCompilation signed = driver.WithOptions(Signed(driver.Options, key));
        Assert.Empty(Errors(signed));
        Assert.Equal(key.PublicKey, signed.Assembly.Identity.PublicKey);
        Assert.NotEmpty(Image(signed));
        Assert.Equal(["CS0281"], Errors(driver));
        Assert.Equal(["CS0281"], Errors(driver.WithOptions(Signed(driver.Options, new DriverKey()))));
    }

    private const string InternalSource = "internal class C { internal static int M() => 1; }";

    /// <summary><paramref name="options"/> signing with <paramref name="key"/>, written under the test's folder.</summary>
    private CSharpCompilationOptions Signed(CSharpCompilationOptions options, DriverKey key) =>
        options.WithCryptoKeyFile(key.Write(Directory.CreateDirectory(Path.Combine(root, Path.GetRandomFileName())).FullName)).WithStrongNameProvider(new DesktopStrongNameProvider());

    /// <summary>A driver-like assembly named <paramref name="name"/> that calls <c>C.M</c>, which is internal.</summary>
    private static CSharpCompilation Driver(string name, MetadataReference project) =>
        Compile("internal static class Program { private static int Main() => C.M(); }", name, project);

    /// <summary>An emitted assembly, read whole so the file is not held open.</summary>
    private static PortableExecutableReference Emitted(string path) => MetadataReference.CreateFromImage(File.ReadAllBytes(path));

    private static string[] Errors(CSharpCompilation compilation) =>
        [.. compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Id)];

    /// <summary>The argument of every <c>InternalsVisibleTo</c> on the assembly emitted at <paramref name="path"/>.</summary>
    private static string[] Friends(string path)
    {
        MetadataReference reference = Emitted(path);
        IAssemblySymbol assembly = (IAssemblySymbol)Compile(string.Empty, "Reader", reference).GetAssemblyOrModuleSymbol(reference)!;
        return
        [
            .. assembly.GetAttributes()
                .Where(static a => string.Equals(a.AttributeClass!.Name, "InternalsVisibleToAttribute", StringComparison.Ordinal))
                .Select(static a => (string)a.ConstructorArguments[0].Value!),
        ];
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
