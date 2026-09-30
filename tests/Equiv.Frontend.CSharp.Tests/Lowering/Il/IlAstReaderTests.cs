using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;

using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering.Il;

using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.IL.Transforms;
using ICSharpCode.Decompiler.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering.Il;

/// <summary>Ticket P1-014: reading a method of a loaded compilation back as ILAst (ADR 0039).</summary>
public sealed class IlAstReaderTests
{
    /// <summary>P1-012's pipeline, pinned: a Decompiler upgrade that adds, renames or reorders a transform fails here.</summary>
    private static readonly string[] Pipeline =
    [
        "ControlFlowSimplification", "SplitVariables", "ILInlining", "InlineReturnTransform", "RemoveInfeasiblePathTransform",
        "DetectPinnedRegions", "DetectCatchWhenConditionBlocks", "DetectExitPoints", "LdLocaDupInitObjTransform",
        "EarlyExpressionTransforms", "SplitVariables", "RemoveDeadVariableInit", "ControlFlowSimplification", "SwitchDetection",
        "SplitVariables", "BlockILTransform[LoopDetection]", "DetectExitPoints", "BlockILTransform[ConditionDetection]",
        "CopyPropagation", "RemoveRedundantReturn",
    ];

    /// <summary>
    /// The Design's pinned list: the pipeline is exactly P1-012's, and every transform left out is one ILSpy runs, so a
    /// relifting transform added by an upgrade changes the pipeline and a renamed one leaves a name unmatched.
    /// </summary>
    [Fact]
    public void EveryReliftingTransformIsRemoved()
    {
        HashSet<string> all = ICSharpCode.Decompiler.CSharp.CSharpDecompiler.GetILTransforms()
            .SelectMany(static t => t is BlockILTransform block ? [t, .. block.PostOrderTransforms] : new object[] { t })
            .Select(static t => t.GetType().Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(Pipeline, IlAstReader.Pipeline(), StringComparer.Ordinal);
        Assert.Subset(all, IlAstReader.Relifting.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(24, IlAstReader.Relifting.Length);
    }

    /// <summary>A symbol no emitted method matches is a reason: one of another assembly, and one with no documentation ID.</summary>
    [Fact]
    public void AMissingMethodIsAReasonNotAnException()
    {
        Compilation compilation = Compile("class C { int M() => new System.Func<int>(() => 1)(); }");
        IMethodSymbol metadata = compilation.GetSpecialType(SpecialType.System_Object).GetMembers("ToString").OfType<IMethodSymbol>().Single();
        IMethodSymbol lambda = Lambda(compilation);

        Assert.Equal(IlAstReader.MethodNotFound, IlAstReader.Read(metadata, compilation).Failure);
        Assert.Equal(IlAstReader.MethodNotFound, IlAstReader.Read(lambda, compilation).Failure);
        IrOpaque opaque = Assert.Single(IlLowererTests.Opaques(IlLowerer.Lower(metadata, compilation)));
        Assert.Equal((IlAstReader.MethodNotFound, true), (opaque.Reason, opaque.WholeBody));
    }

    /// <summary>A symbol of another compilation, which the emitted module does not declare, is not found either.</summary>
    [Fact]
    public void AMethodOfAnotherCompilationIsNotFound()
    {
        Compilation compilation = Compile("class C { int M() => 1; }");
        IMethodSymbol other = Method(Compile("class D { int N() => 1; }"), "D", "N");

        Assert.Equal(IlAstReader.MethodNotFound, IlAstReader.Read(other, compilation).Failure);
    }

    [Fact]
    public void ACompilationThatDoesNotEmitIsAReason()
    {
        Compilation compilation = Compile("class C { int M() => undefined; }");

        IrOpaque opaque = Assert.Single(IlLowererTests.Opaques(IlLowerer.Lower(Method(compilation, "C", "M"), compilation)));

        Assert.Equal(IlAstReader.EmitFailed, opaque.Reason);
    }

    [Theory]
    [InlineData("abstract class C { public abstract int M(); }")]
    [InlineData("class C { static extern int M(); }")]
    public void AMethodWithNoIlIsAReason(string source)
    {
        Compilation compilation = Compile(source);

        Assert.Equal(IlAstReader.NoBody, IlAstReader.Read(Method(compilation, "C", "M"), compilation).Failure);
    }

    /// <summary>The PDB names a local as the source does, and a sequence point gives an opaque the line of its statement.</summary>
    [Fact]
    public void LocalsAndSpansComeFromThePdb()
    {
        Compilation compilation = Compile("class C\n{\n    int f;\n    int M(int a)\n    {\n        int total = a + 1;\n        return total + ~f;\n    }\n}\n");

        IlAstReader.Body body = IlAstReader.Read(Method(compilation, "C", "M"), compilation);
        IrOpaque field = IlLowererTests.Opaques(IlLowerer.Lower(Method(compilation, "C", "M"), compilation)).First(static o => string.Equals(o.Reason, "BitNot", StringComparison.Ordinal));

        Assert.Contains(body.Function!.Variables, static v => v is { Name: "total", HasGeneratedName: false });
        Assert.Equal(("Snippet.cs", 7, 9), (field.Span.Path, field.Span.StartLine, field.Span.StartColumn));
    }

    /// <summary>A PDB document named through a <c>PathMap</c> is spelled as its syntax tree's own path, as a span of the IOperation lowering is.</summary>
    [Fact]
    public void ADocumentNamedThroughAPathMapIsItsTreesPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "equiv-il");
        string path = Path.Combine(directory, "Mapped.cs");
        Compilation compilation = CSharpCompilation.Create(
            "Mapped",
            [CSharpSyntaxTree.ParseText(SourceText.From("class C { int f; int M() => ~f; }", Encoding.UTF8), path: path, cancellationToken: TestContext.Current.CancellationToken)],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithSourceReferenceResolver(new SourceFileResolver([], directory, [KeyValuePair.Create(directory + Path.DirectorySeparatorChar, "/_/")])));

        IrOpaque field = Assert.Single(IlLowererTests.Opaques(IlLowerer.Lower(Method(compilation, "C", "M"), compilation)));

        Assert.Equal(path, field.Span.Path);
    }

    /// <summary>A resolver that normalises a relative path to nothing leaves the document named as the tree is.</summary>
    [Fact]
    public void ADocumentTheResolverCannotNormaliseKeepsItsName()
    {
        Compilation compilation = Compile("class C { int f; int M() => ~f; }").WithOptions(
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithSourceReferenceResolver(new SourceFileResolver([], baseDirectory: null)));

        IrOpaque field = Assert.Single(IlLowererTests.Opaques(IlLowerer.Lower(Method(compilation, "C", "M"), compilation)));

        Assert.Equal("Snippet.cs", field.Span.Path);
    }

    /// <summary>A method whose every sequence point is hidden gives its opaques the method's own span.</summary>
    [Fact]
    public void AMethodWithNoVisibleSequencePointUsesItsOwnSpan()
    {
        Compilation compilation = Compile("class C\n{\n    int f;\n#line hidden\n    int M() => ~f;\n#line default\n}\n");

        IrOpaque field = Assert.Single(IlLowererTests.Opaques(IlLowerer.Lower(Method(compilation, "C", "M"), compilation)));

        Assert.Equal(5, field.Span.StartLine);
    }

    /// <summary>The PDB as ILSpy asks it: each local by index, none for an index it has no name for, and no extra type information.</summary>
    [Fact]
    public void ThePdbAnswersAsADebugInfoProvider()
    {
        Compilation compilation = Compile("class C { int M(int a) { int total = a; return total; } }");
        IlAstReader.Body body = IlAstReader.Read(Method(compilation, "C", "M"), compilation);
        IlAstReader.Pdb pdb = body.Module!.Pdb;
        MethodDefinitionHandle handle = (MethodDefinitionHandle)body.Function!.Method!.MetadataToken;

        Assert.Equal(["total"], pdb.GetVariables(handle).Select(static v => v.Name), StringComparer.Ordinal);
        Assert.True(pdb.TryGetName(handle, 0, out string name));
        Assert.Equal("total", name);
        Assert.False(pdb.TryGetName(handle, 7, out _));
        Assert.False(pdb.TryGetExtraTypeInfo(handle, 0, out PdbExtraTypeInfo _));
        Assert.NotEmpty(pdb.Description);
        Assert.Empty(pdb.SourceFileName);
        Assert.NotEmpty(pdb.GetSequencePoints(handle));
    }

    /// <summary>The resolver answers by simple name, from the compilation's references, and never has a module or a snapshot to take.</summary>
    [Fact]
    public void TheResolverAnswersFromTheCompilationsReferences()
    {
        IlAstReader.Resolver resolver = new(Compile("class C { }"));

        Assert.NotNull(resolver.Resolve(AssemblyNameReference.Parse("System.Runtime")));
        Assert.Null(resolver.Resolve(AssemblyNameReference.Parse("No.Such.Assembly")));
        Assert.Null(resolver.ResolveModule(Module().File, "module"));
        using IDisposable snapshot = resolver.BeginSnapshot();
    }

    [Fact]
    public async Task TheResolverAnswersAsynchronouslyAsItDoesSynchronously()
    {
        IlAstReader.Resolver resolver = new(Compile("class C { }"));

        Assert.NotNull(await resolver.ResolveAsync(AssemblyNameReference.Parse("System.Runtime")));
        Assert.Null(await resolver.ResolveModuleAsync(Module().File, "module"));
    }

    private static IlAstReader.Module Module()
    {
        Compilation compilation = Compile("class C { int M() => 1; }");
        return IlAstReader.Read(Method(compilation, "C", "M"), compilation).Module!;
    }

    /// <summary>
    /// A project reference is emitted as its own compilation is, a reference with no file has no module, and a file that has
    /// gone or is no longer a module leaves its types unresolved: the call into it is then opaque, never an exception.
    /// </summary>
    [Theory]
    [InlineData("project")]
    [InlineData("image")]
    [InlineData("deleted")]
    [InlineData("garbage")]
    [InlineData("broken")]
    public void EveryKindOfReferenceLowers(string kind)
    {
        Compilation library = Compile(
            "public static class L { public static int G; public static int F(int x) => x + G; public static bool B() => true; public static string S() => \"s\"; }"
            + " public class Box<T> { } public class Item { }",
            "Library");
        MetadataReference reference = kind switch
        {
            "project" => library.ToMetadataReference(),
            "broken" => Compile("public static class L { public static int G; public static int F(int x) => x + G + undefined; public static bool B() => true; public static string S() => \"s\"; }"
                + " public class Box<T> { } public class Item { }").ToMetadataReference(),
            "image" => MetadataReference.CreateFromImage(Image(library)),
            _ => File(library, kind),
        };
        Compilation compilation = RoslynTestCompilations.Compile(
            "class C { int M(int a) { object b = new Box<int>(); object l = new System.Collections.Generic.List<Item>(); bool f = L.B() & a > 0; return L.F(a) + L.G + L.S().Length; } }",
            [reference]);

        IrProcedure procedure = IlLowerer.Lower(Method(compilation, "C", "M"), compilation);

        Assert.Empty(IrValidator.Validate(procedure));
    }

    private static MetadataReference File(Compilation library, string kind)
    {
        string path = Path.Combine(Path.GetTempPath(), $"equiv-il-{Guid.NewGuid():N}.dll");
        System.IO.File.WriteAllBytes(path, [.. Image(library)]);
        MetadataReference reference = MetadataReference.CreateFromFile(path);
        if (string.Equals(kind, "deleted", StringComparison.Ordinal))
        {
            System.IO.File.Delete(path);
        }
        else
        {
            System.IO.File.WriteAllBytes(path, [1, 2, 3]);
        }

        return reference;
    }

    private static ImmutableArray<byte> Image(Compilation compilation)
    {
        using MemoryStream stream = new();
        Assert.True(compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success);
        return [.. stream.ToArray()];
    }

    private static IMethodSymbol Lambda(Compilation compilation)
    {
        SyntaxTree tree = compilation.SyntaxTrees.Single();
        Microsoft.CodeAnalysis.CSharp.Syntax.LambdaExpressionSyntax syntax = tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LambdaExpressionSyntax>().Single();
        return (IMethodSymbol)compilation.GetSemanticModel(tree).GetSymbolInfo(syntax, TestContext.Current.CancellationToken).Symbol!;
    }

    internal static Compilation Compile(string source, string assemblyName = "Snippet") => RoslynTestCompilations.Compile(source, assemblyName);

    internal static IMethodSymbol Method(Compilation compilation, string type, string name) =>
        compilation.GetTypeByMetadataName(type)!.GetMembers(name).OfType<IMethodSymbol>().Single();
}
