using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-036: M4-007's <c>RenameLocals</c> seed on ServiceAnt's <c>CanHandleEventByIocHandler()</c>, standalone. The
/// method passes a new <c>params IWindsorInstaller[]</c> to an external call and its renamed local to MSTest 4's
/// <c>Assert.AreEqual</c>, whose <c>[CallerArgumentExpression]</c> parameter receives the local's name as a string. A
/// rename alone verifies; a rename whose name reaches such a parameter really changes a call argument, so it is Divergent.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PreservingRenameEquivalenceTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    /// <summary>The shape of ServiceAnt's Castle and MSTest dependencies, compiled as a separate assembly, as they are there.</summary>
    private static readonly MetadataReference Library = Compile("Library", """
        using System.Runtime.CompilerServices;
        public interface IInstaller { }
        public interface IBus { System.Threading.Tasks.Task Publish(object e); }
        public sealed class Installer : IInstaller { public Installer(params System.Reflection.Assembly[] assemblies) { } }
        public sealed class Container { public Container Install(params IInstaller[] installers) => this; public T Resolve<T>() => default!; }
        public static class Assert
        {
            public static void AreEqual<T>(T expected, T actual) { }
            public static void AreEqualNamed<T>(T expected, T actual, [CallerArgumentExpression(nameof(expected))] string expectedExpression = "") { }
        }
        """, []).ToMetadataReference();

    private const string Method = """
        class C
        {
            private sealed class Tray { public string Result { get; set; } = ""; }
            private static string RESULT_CONTAINER = "";
            public async System.Threading.Tasks.Task M()
            {
                var NAME = "HelloWorld";
                var newContainer = new Container();
                newContainer.Install(new Installer(System.Reflection.Assembly.GetExecutingAssembly()));
                await newContainer.Resolve<IBus>().Publish(new Tray() { Result = NAME });
                ASSERT(NAME, RESULT_CONTAINER);
            }
        }
        """;

    [Fact]
    public void ALocalRenameNextToAParamsArrayOfAnInterfaceVerifies() =>
        Assert.IsType<Equivalent>(Verify("AreEqual"));

    [Fact]
    public void ALocalRenameThatACallerArgumentExpressionSeesIsDivergent() =>
        Assert.IsType<Divergent>(Verify("AreEqualNamed"));

    private static Verdict Verify(string assert) =>
        new Z3Backend().Verify(Lower(Renamed(assert, "testValue"), isLegacy: true), Lower(Renamed(assert, "testValue0"), isLegacy: false), Options);

    private static string Renamed(string assert, string local) =>
        Method.Replace("NAME", local, StringComparison.Ordinal).Replace("ASSERT", "Assert." + assert, StringComparison.Ordinal);

    private static CSharpCompilation Compile(string name, string source, ImmutableArray<MetadataReference> extra)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [.. References, .. extra],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    private static IrProcedure Lower(string source, bool isLegacy)
    {
        CSharpCompilation compilation = Compile("Snippet", source, [Library]);
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy, Runtimes.Migration).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }
}
