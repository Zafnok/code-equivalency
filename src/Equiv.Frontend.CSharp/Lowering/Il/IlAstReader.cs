using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;

using Equiv.Core;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.IL.Transforms;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

using PortableFile = ICSharpCode.Decompiler.Metadata.PEFile;
using SequencePoint = ICSharpCode.Decompiler.DebugInfo.SequencePoint;

namespace Equiv.Frontend.CSharp.Lowering.Il;

/// <summary>
/// Reads one method of a loaded <see cref="Compilation"/> back as ILSpy's ILAst (ADR 0039; ticket P1-014). The compilation
/// is emitted in memory with a portable PDB, once per compilation for as long as the compilation lives, and read by
/// <see cref="ILReader"/> with that PDB, so locals keep their source names and every instruction has a sequence point.
/// The transforms are <see cref="ICSharpCode.Decompiler.CSharp.CSharpDecompiler.GetILTransforms"/> less
/// <see cref="Relifting"/>, the ones that rebuild a C# construct from its IL (P1-012's pipeline): what stays structures
/// control flow, inlines stack slots and splits variables. A failed emit or lookup is a reason, never an exception.
/// </summary>
internal static class IlAstReader
{
    /// <summary>The compilation does not emit, as erroneous code does not.</summary>
    public const string EmitFailed = "il-emit-failed";

    /// <summary>The symbol has no definition with a body handle in the emitted module, such as a symbol of another assembly.</summary>
    public const string MethodNotFound = "il-method-not-found";

    /// <summary>The method has no IL: abstract, <c>extern</c> or a runtime-implemented delegate member.</summary>
    public const string NoBody = "il-no-body";

    /// <summary>
    /// The transforms left out, by type name: async and iterator state machines, dynamic call sites, string and nullable
    /// switches, patterns, <c>lock</c>, <c>using</c>, cached delegates and spans, the statement transforms, proxy calls,
    /// increments, lambdas, local functions and display classes, high-level loops, nesting, the local-type annotations,
    /// nested conditionals and variable naming. Each rebuilds a construct the fallback exists to avoid.
    /// </summary>
    public static readonly ImmutableArray<string> Relifting =
    [
        "YieldReturnDecompiler", "AsyncAwaitDecompiler", "DynamicCallSiteTransform", "SwitchOnStringTransform",
        "SwitchOnNullableTransform", "PatternMatchingTransform", "LockTransform", "UsingTransform",
        "CachedDelegateInitialization", "CachedReadOnlySpanInitialization", "StatementTransform", "ProxyCallReplacer",
        "FixRemainingIncrements", "DelegateConstruction", "LocalFunctionDecompiler", "TransformDisplayClassUsage",
        "HighLevelLoopTransform", "ReduceNestingTransform", "IntroduceDynamicTypeOnLocals", "IntroduceNativeIntTypeOnLocals",
        "IntroduceScopedModifierOnLocals", "IntroduceRefReadOnlyModifierOnLocals", "ExpandNestedConditionals", "AssignVariableNames",
    ];

    private static readonly FrozenSet<string> Removed = Relifting.ToFrozenSet(StringComparer.Ordinal);

    private static readonly ConditionalWeakTable<Compilation, Module?> Modules = [];

    /// <summary>
    /// A missing reference leaves its types unknown instead of failing the read; the type system spells types as metadata
    /// does, without the tuple, <c>dynamic</c>, native-integer or nullable-annotation views, as <see cref="TypeMapper"/> does.
    /// </summary>
    private static readonly DecompilerSettings Settings = new()
    {
        ThrowOnAssemblyResolveErrors = false,
        TupleTypes = false,
        Dynamic = false,
        NativeIntegers = false,
        NullableReferenceTypes = false,
    };

    /// <summary>
    /// The transforms run, in order, a block transform with the block transforms it runs in brackets: a fresh list per
    /// call, since a block transform's list is its own mutable state.
    /// </summary>
    public static ImmutableArray<string> Pipeline() => [.. Transforms().Select(Name)];

    /// <summary>The ILAst of <paramref name="method"/>, a method of <paramref name="compilation"/>, or why there is none.</summary>
    public static Body Read(IMethodSymbol method, Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(compilation);
        if (Modules.GetValue(compilation, Emit) is not { } module)
        {
            return new Body(Function: null, Module: null, EmitFailed);
        }

        if (method.GetDocumentationCommentId() is not { } id
            || IdStringProvider.FindEntity(id, new SimpleTypeResolveContext(module.TypeSystem.MainModule)) is not IMethod definition
            || definition.ParentModule != module.TypeSystem.MainModule)
        {
            return new Body(Function: null, Module: null, MethodNotFound);
        }

        MethodDefinitionHandle handle = (MethodDefinitionHandle)definition.MetadataToken;
        int address = module.File.Metadata.GetMethodDefinition(handle).RelativeVirtualAddress;
        if (address == 0)
        {
            return new Body(Function: null, Module: null, NoBody);
        }

        ILReader reader = new(module.TypeSystem.MainModule) { UseDebugSymbols = true, DebugInfo = module.Pdb };
        ILFunction function = reader.ReadIL(
            handle,
            module.File.GetMethodBody(address),
            new GenericContext(definition.DeclaringTypeDefinition!.TypeParameters, definition.TypeParameters),
            ILFunctionKind.TopLevelFunction,
            CancellationToken.None);
        function.RunTransforms(Transforms(), new ILTransformContext(function, module.TypeSystem, module.Pdb, Settings));
        return new Body(function, module, Failure: null);
    }

    private static List<IILTransform> Transforms()
    {
        List<IILTransform> kept = [];
        foreach (IILTransform transform in ICSharpCode.Decompiler.CSharp.CSharpDecompiler.GetILTransforms().Where(static t => !Removed.Contains(t.GetType().Name)))
        {
            foreach (IBlockTransform inner in (transform as BlockILTransform)?.PostOrderTransforms.Where(static t => Removed.Contains(t.GetType().Name)).ToList() ?? [])
            {
                ((BlockILTransform)transform).PostOrderTransforms.Remove(inner);
            }

            kept.Add(transform);
        }

        return kept;
    }

    private static string Name(IILTransform transform) => transform is BlockILTransform block
        ? $"{nameof(BlockILTransform)}[{string.Join(", ", block.PostOrderTransforms.Select(static t => t.GetType().Name))}]"
        : transform.GetType().Name;

    /// <summary>
    /// The compilation's image and PDB and a type system over them, or null when it does not emit. A syntax tree with no
    /// encoding, as one parsed from a string has, is given UTF-8 first, since a PDB records each document's checksum.
    /// </summary>
    private static Module? Emit(Compilation compilation)
    {
        Compilation encoded = compilation;
        foreach (SyntaxTree tree in compilation.SyntaxTrees.Where(static t => t.Encoding is null))
        {
            encoded = encoded.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(SourceText.From(tree.GetText().ToString(), Encoding.UTF8), (CSharpParseOptions)tree.Options, tree.FilePath));
        }

        using MemoryStream image = new();
        using MemoryStream pdb = new();
        if (!encoded.Emit(image, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb)).Success)
        {
            return null;
        }

        PortableFile file = Image($"{compilation.AssemblyName}.dll", image.ToArray());
        return new Module(
            file,
            new DecompilerTypeSystem(file, new Resolver(compilation), Settings),
            new Pdb(MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(pdb.ToArray())), Documents(compilation)));
    }

    /// <summary>
    /// Each syntax tree's path by the name its PDB document gets: the path, normalised by the compilation's
    /// <see cref="CompilationOptions.SourceReferenceResolver"/> (which applies any <c>PathMap</c>) when it has one.
    /// </summary>
    private static ImmutableDictionary<string, string> Documents(Compilation compilation) =>
        compilation.SyntaxTrees
            .Select(tree => (Name: compilation.Options.SourceReferenceResolver?.NormalizePath(tree.FilePath, baseFilePath: null) ?? tree.FilePath, tree.FilePath))
            .DistinctBy(static d => d.Name, StringComparer.Ordinal)
            .ToImmutableDictionary(static d => d.Name, static d => d.FilePath, StringComparer.Ordinal);

    /// <summary>A reference's module: a project reference emitted as its compilation is, a file read from its path, else none.</summary>
    private static PortableFile? Load(MetadataReference? reference) => reference switch
    {
        CompilationReference project => Modules.GetValue(project.Compilation, Emit)?.File,
        PortableExecutableReference { FilePath: { } path } => FromPath(path),
        _ => null,
    };

    private static PortableFile Image(string name, byte[] image) => new(name, new MemoryStream(image), PEStreamOptions.PrefetchEntireImage);

    /// <summary>A module read from <paramref name="path"/>, or none when the file has gone or is no longer a module.</summary>
    private static PortableFile? FromPath(string path)
    {
        try
        {
            return Image(path, File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>Resolves an assembly reference from the compilation's own references, by simple name, each once.</summary>
    internal sealed class Resolver : IAssemblyResolver
    {
        private readonly Dictionary<string, Lazy<PortableFile?>> byName = new(StringComparer.OrdinalIgnoreCase);

        public Resolver(Compilation compilation)
        {
            foreach (IAssemblySymbol assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                byName.TryAdd(assembly.Identity.Name, new Lazy<PortableFile?>(() => Load(compilation.GetMetadataReference(assembly))));
            }
        }

        public MetadataFile? Resolve(IAssemblyReference reference) => byName.TryGetValue(reference.Name, out Lazy<PortableFile?>? file) ? file.Value : null;

        public MetadataFile? ResolveModule(MetadataFile mainModule, string moduleName) => null;

        public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference) => Task.FromResult(Resolve(reference));

        public Task<MetadataFile?> ResolveModuleAsync(MetadataFile mainModule, string moduleName) => Task.FromResult<MetadataFile?>(null);

        public IDisposable BeginSnapshot() => new MemoryStream();
    }

    /// <summary>
    /// The emitted PDB as ILSpy reads debug information: each method's sequence points, with its document named by the
    /// syntax tree's own path, and its locals' names.
    /// </summary>
    internal sealed class Pdb(MetadataReaderProvider provider, ImmutableDictionary<string, string> documents) : IDebugInfoProvider
    {
        private readonly MetadataReader reader = provider.GetMetadataReader();

        public string Description => "in-memory portable PDB";

        public string SourceFileName => string.Empty;

        public IList<SequencePoint> GetSequencePoints(MethodDefinitionHandle method) =>
        [
            .. reader.GetMethodDebugInformation(method).GetSequencePoints().Where(static p => !p.IsHidden).Select(p => new SequencePoint
            {
                Offset = p.Offset,
                StartLine = p.StartLine,
                StartColumn = p.StartColumn,
                EndLine = p.EndLine,
                EndColumn = p.EndColumn,
                DocumentUrl = Document(reader.GetString(reader.GetDocument(p.Document).Name)),
            }),
        ];

        public IList<Variable> GetVariables(MethodDefinitionHandle method) =>
        [
            .. reader.GetLocalScopes(method)
                .SelectMany(s => reader.GetLocalScope(s).GetLocalVariables())
                .Select(v => reader.GetLocalVariable(v))
                .Select(v => new Variable(v.Index, reader.GetString(v.Name))),
        ];

        public bool TryGetName(MethodDefinitionHandle method, int index, out string name)
        {
            name = string.Concat(GetVariables(method).Where(v => v.Index == index).Select(static v => v.Name));
            return name.Length > 0;
        }

        public bool TryGetExtraTypeInfo(MethodDefinitionHandle method, int index, out PdbExtraTypeInfo extraTypeInfo)
        {
            extraTypeInfo = default;
            return false;
        }

        /// <summary>
        /// <paramref name="method"/>'s source span nearest <paramref name="offset"/>: the last sequence point at or before it,
        /// else the first after it; null when the method has none that is not hidden.
        /// </summary>
        public SourceSpan? Span(MethodDefinitionHandle method, int offset) =>
            GetSequencePoints(method).MinBy(p => (p.Offset > offset, Math.Abs(offset - p.Offset))) is { } point
                ? new SourceSpan(point.DocumentUrl, point.StartLine, point.StartColumn, point.EndLine, point.EndColumn)
                : null;

        private string Document(string name) => documents.GetValueOrDefault(name, name);
    }

    /// <summary>A compilation's emitted module: its image, the type system over it and its references, and its PDB.</summary>
    internal sealed record Module(PortableFile File, DecompilerTypeSystem TypeSystem, Pdb Pdb);

    /// <summary>One method's ILAst and the module it was read from, or the reason there is none.</summary>
    internal sealed record Body(ILFunction? Function, Module? Module, string? Failure);
}
