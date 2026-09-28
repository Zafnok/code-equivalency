using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.IL.Transforms;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

using Microsoft.CodeAnalysis;

namespace IlLoweringSpike;

/// <summary>One side's method as ILAst: its instruction keys, and two signatures for comparing the sides.</summary>
/// <param name="Keys">Every instruction's <see cref="MappingTable.Key"/>, distinct.</param>
/// <param name="Text">The ILAst as ILSpy writes it, with IL offsets, labels and variable names renumbered by first appearance.</param>
/// <param name="Shape">The opcode tree alone: no member, type, variable or constant.</param>
internal sealed record IlAst(IReadOnlySet<string> Keys, string Text, string Shape);

/// <summary>
/// Emits a project's <see cref="Compilation"/> in memory (as M4-009's replay does, but not to disk) and reads one method's
/// body back into ILSpy's ILAst. The pipeline is <see cref="CSharpDecompiler.GetILTransforms"/> without the transforms that
/// rebuild a C# construct from its IL (async, iterators, <c>using</c>, <c>lock</c>, lambdas, string switches, patterns,
/// initialisers and the other statement transforms): those would reintroduce exactly the constructs the fallback exists to
/// avoid. What stays structures control flow, inlines stack slots and splits variables.
/// </summary>
internal sealed partial class IlAstReader
{
    private static readonly HashSet<string> Relifting = new(StringComparer.Ordinal)
    {
        "YieldReturnDecompiler", "AsyncAwaitDecompiler", "DynamicCallSiteTransform", "SwitchOnStringTransform",
        "SwitchOnNullableTransform", "PatternMatchingTransform", "LockTransform", "UsingTransform",
        "CachedDelegateInitialization", "CachedReadOnlySpanInitialization", "StatementTransform", "ProxyCallReplacer",
        "FixRemainingIncrements", "DelegateConstruction", "LocalFunctionDecompiler", "TransformDisplayClassUsage",
        "HighLevelLoopTransform", "ReduceNestingTransform", "IntroduceDynamicTypeOnLocals", "IntroduceNativeIntTypeOnLocals",
        "IntroduceScopedModifierOnLocals", "IntroduceRefReadOnlyModifierOnLocals", "ExpandNestedConditionals", "AssignVariableNames",
    };

    private readonly Dictionary<Compilation, Module?> modules = new(ReferenceEqualityComparer.Instance);
    private readonly DecompilerSettings settings = new() { ThrowOnAssemblyResolveErrors = false };

    public List<string> Transforms { get; } = [];

    public IlAstReader()
    {
        foreach (IILTransform transform in Pipeline())
        {
            Transforms.Add(transform is BlockILTransform block
                ? $"{nameof(BlockILTransform)}[{string.Join(", ", block.PostOrderTransforms.Select(static t => t.GetType().Name))}]"
                : transform.GetType().Name);
        }
    }

    private List<IILTransform> Pipeline()
    {
        List<IILTransform> kept = [];
        foreach (IILTransform transform in CSharpDecompiler.GetILTransforms())
        {
            if (Relifting.Contains(transform.GetType().Name))
            {
                continue;
            }

            if (transform is BlockILTransform block)
            {
                foreach (IBlockTransform inner in block.PostOrderTransforms.Where(static t => Relifting.Contains(t.GetType().Name)).ToList())
                {
                    block.PostOrderTransforms.Remove(inner);
                }
            }

            kept.Add(transform);
        }

        return kept;
    }

    /// <summary>The method's ILAst, or the reason there is none.</summary>
    public (IlAst? Ast, string? Failure) Read(IMethodSymbol symbol, Compilation compilation)
    {
        Module? module = ModuleOf(compilation);
        if (module is null)
        {
            return (null, "emit-failed");
        }

        string? id = symbol.GetDocumentationCommentId();
        if (id is null || IdStringProvider.FindEntity(id, new SimpleTypeResolveContext(module.TypeSystem.MainModule)) is not IMethod method
            || method.MetadataToken.IsNil || method.ParentModule != module.TypeSystem.MainModule)
        {
            return (null, "method-not-found");
        }

        MethodDefinitionHandle handle = (MethodDefinitionHandle)method.MetadataToken;
        MethodDefinition definition = module.File.Metadata.GetMethodDefinition(handle);
        if (definition.RelativeVirtualAddress == 0)
        {
            return (null, "no-body");
        }

        MethodBodyBlock body = module.File.GetMethodBody(definition.RelativeVirtualAddress);
        ILReader reader = new(module.TypeSystem.MainModule) { UseDebugSymbols = false };
        ILFunction function = reader.ReadIL(handle, body, new GenericContext(method.DeclaringTypeDefinition?.TypeParameters, method.TypeParameters), ILFunctionKind.TopLevelFunction, CancellationToken.None);
        ILTransformContext context = new(function, module.TypeSystem, debugInfo: null, settings);
        function.RunTransforms(Pipeline(), context);

        HashSet<ILVariable> caught = MappingTable.CaughtException(function);
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (ILInstruction instruction in function.Descendants)
        {
            keys.Add(MappingTable.Key(instruction, caught));
        }

        PlainTextOutput text = new();
        function.Body.WriteTo(text, new ILAstWritingOptions());
        StringBuilder shape = new();
        Shape(function.Body, shape);
        return (new IlAst(keys, Canonical(text.ToString()), shape.ToString()), null);
    }

    private static void Shape(ILInstruction instruction, StringBuilder into)
    {
        into.Append(instruction.OpCode).Append('(');
        foreach (ILInstruction child in instruction.Children)
        {
            Shape(child, into);
        }

        into.Append(')');
    }

    /// <summary>IL offsets and labels, and ILAst's variable names, renumbered by first appearance: an SSA lowering erases both.</summary>
    private static string Canonical(string text)
    {
        Dictionary<string, int> labels = new(StringComparer.Ordinal);
        Dictionary<string, int> variables = new(StringComparer.Ordinal);
        text = Label().Replace(text, m => $"L{Number(labels, m.Value)}");
        return Variable().Replace(text, m => $"{m.Groups[1].Value}#{Number(variables, m.Value)}");
    }

    private static int Number(Dictionary<string, int> seen, string key)
    {
        if (!seen.TryGetValue(key, out int n))
        {
            seen[key] = n = seen.Count;
        }

        return n;
    }

    [GeneratedRegex(@"\bIL_[0-9a-fA-F]{4,}\b")]
    private static partial Regex Label();

    [GeneratedRegex(@"\b([A-Z])_\d+\b")]
    private static partial Regex Variable();

    private Module? ModuleOf(Compilation compilation)
    {
        if (!modules.TryGetValue(compilation, out Module? module))
        {
            modules[compilation] = module = Emit(compilation);
        }

        return module;
    }

    private Module? Emit(Compilation compilation)
    {
        MetadataFile? file = Image(compilation);
        if (file is null)
        {
            return null;
        }

        DecompilerTypeSystem typeSystem = new(file, new Resolver(this, compilation), settings);
        return new Module(file, typeSystem);
    }

    private readonly Dictionary<Compilation, MetadataFile?> images = new(ReferenceEqualityComparer.Instance);

    private MetadataFile? Image(Compilation compilation)
    {
        if (!images.TryGetValue(compilation, out MetadataFile? file))
        {
            MemoryStream stream = new();
            file = compilation.Emit(stream).Success
                ? new ICSharpCode.Decompiler.Metadata.PEFile(compilation.AssemblyName + ".dll", new MemoryStream(stream.ToArray()), PEStreamOptions.PrefetchEntireImage)
                : null;
            images[compilation] = file;
        }

        return file;
    }

    private readonly Dictionary<string, MetadataFile?> files = new(StringComparer.OrdinalIgnoreCase);

    private MetadataFile? FromPath(string path)
    {
        if (!files.TryGetValue(path, out MetadataFile? file))
        {
            try
            {
                file = new ICSharpCode.Decompiler.Metadata.PEFile(path, File.OpenRead(path), PEStreamOptions.PrefetchEntireImage);
            }
            catch (Exception exception) when (exception is IOException or BadImageFormatException or MetadataFileNotSupportedException)
            {
                file = null;
            }

            files[path] = file;
        }

        return file;
    }

    private sealed record Module(MetadataFile File, DecompilerTypeSystem TypeSystem);

    /// <summary>Resolves an assembly reference from the compilation's own references, by simple name.</summary>
    private sealed class Resolver : IAssemblyResolver
    {
        private readonly IlAstReader owner;
        private readonly Dictionary<string, Func<MetadataFile?>> byName = new(StringComparer.OrdinalIgnoreCase);

        public Resolver(IlAstReader owner, Compilation compilation)
        {
            this.owner = owner;
            foreach (MetadataReference reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                {
                    continue;
                }

                string name = assembly.Identity.Name;
                if (byName.ContainsKey(name))
                {
                    continue;
                }

                byName[name] = reference switch
                {
                    CompilationReference project => () => owner.Image(project.Compilation),
                    PortableExecutableReference { FilePath: { } path } => () => owner.FromPath(path),
                    _ => static () => null,
                };
            }
        }

        public MetadataFile? Resolve(IAssemblyReference reference) =>
            byName.TryGetValue(reference.Name, out Func<MetadataFile?>? load) ? load() : null;

        public MetadataFile? ResolveModule(MetadataFile mainModule, string moduleName) => null;

        public IDisposable BeginSnapshot() => new MemoryStream();

        public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference) => Task.FromResult(Resolve(reference));

        public Task<MetadataFile?> ResolveModuleAsync(MetadataFile mainModule, string moduleName) => Task.FromResult<MetadataFile?>(null);
    }
}
