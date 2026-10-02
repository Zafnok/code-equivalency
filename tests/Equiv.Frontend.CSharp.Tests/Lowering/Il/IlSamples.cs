using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering.Il;

/// <summary>
/// Each side of each <c>samples/</c> pair as one compilation of its sources, read from disk with their encoding as a loaded
/// project's are, against this test run's own framework (ticket P1-014). A project's package references are not restored
/// here, so the few web types <c>webapi-basic</c> uses come from a stub compilation it references. A side that does not
/// compile by design (<see cref="DoesNotCompile"/>) is left out: it emits no IL.
/// </summary>
internal static class IlSamples
{
    private const string WebStubs = """
        namespace System.Web.Http
        {
            public interface IHttpActionResult { }
            public sealed class NotFoundResult : IHttpActionResult { }
            public sealed class OkNegotiatedContentResult<T> : IHttpActionResult { }
            public abstract class ApiController
            {
                protected NotFoundResult NotFound() => new NotFoundResult();
                protected OkNegotiatedContentResult<T> Ok<T>(T content) => new OkNegotiatedContentResult<T>();
            }
            public sealed class RoutePrefixAttribute(string prefix) : Attribute { public string Prefix => prefix; }
            public sealed class RouteAttribute(string template) : Attribute { public string Template => template; }
            public sealed class HttpGetAttribute : Attribute { }
        }
        namespace Microsoft.AspNetCore.Mvc
        {
            public interface IActionResult { }
            public sealed class NotFoundResult : IActionResult { }
            public sealed class OkObjectResult : IActionResult { }
            public abstract class ControllerBase
            {
                public NotFoundResult NotFound() => new NotFoundResult();
                public OkObjectResult Ok(object value) => new OkObjectResult();
            }
            public sealed class ApiControllerAttribute : System.Attribute { }
            public sealed class RouteAttribute(string template) : System.Attribute { public string Template => template; }
            public sealed class HttpGetAttribute(string template) : System.Attribute { public string Template => template; }
        }
        """;

    private static readonly string[] Sides = ["legacy", "modern"];

    /// <summary>The sides whose sources hold compiler errors on purpose (ticket P2-085); every other side must compile.</summary>
    private static readonly string[] DoesNotCompile = ["partly-compiling-modern/modern"];

    private static readonly Lazy<ImmutableArray<(string Name, Compilation Compilation)>> Loaded = new(Load);

    public static ImmutableArray<(string Name, Compilation Compilation)> All => Loaded.Value;

    public static string RepoRoot
    {
        get
        {
            DirectoryInfo directory = new(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(directory.FullName, "Equiv.slnx")))
            {
                directory = directory.Parent!;
            }

            return directory.FullName;
        }
    }

    private static ImmutableArray<(string, Compilation)> Load()
    {
        MetadataReference stubs = CSharpCompilation.Create(
            "WebStubs",
            [CSharpSyntaxTree.ParseText(WebStubs, cancellationToken: TestContext.Current.CancellationToken)],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).ToMetadataReference();
        return
        [
            .. Directory.GetDirectories(Path.Combine(RepoRoot, "samples"))
                .Order(StringComparer.Ordinal)
                .SelectMany(static sample => Sides.Select(side => (Name: $"{Path.GetFileName(sample)}/{side}", Directory: Path.Combine(sample, side))))
                .Where(static side => !DoesNotCompile.Contains(side.Name, StringComparer.Ordinal))
                .Select(side => (side.Name, Compile(side.Directory, stubs))),
        ];
    }

    private static Compilation Compile(string directory, MetadataReference stubs)
    {
        Compilation compilation = CSharpCompilation.Create(
            Path.GetFileName(Path.GetDirectoryName(directory)) + "." + Path.GetFileName(directory),
            [
                .. Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Where(static f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)
                    .Select(static f => CSharpSyntaxTree.ParseText(
                        SourceText.From(File.ReadAllText(f), Encoding.UTF8),
                        new CSharpParseOptions(LanguageVersion.Latest),
                        f,
                        TestContext.Current.CancellationToken)),
            ],
            [.. RoslynTestCompilations.References, stubs],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return compilation;
    }
}
