using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using Equiv.Core.Configuration;
using Equiv.Core.RuntimeChanges;

using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem.Implementation;

using Microsoft.CodeAnalysis;

using IlMethod = ICSharpCode.Decompiler.TypeSystem.IMethod;
using IlSpecialType = ICSharpCode.Decompiler.TypeSystem.SpecialType;
using IlType = ICSharpCode.Decompiler.TypeSystem.IType;
using IlTypeKind = ICSharpCode.Decompiler.TypeSystem.TypeKind;
using SymbolKind = ICSharpCode.Decompiler.TypeSystem.SymbolKind;

namespace Equiv.Frontend.CSharp.Lowering.Il;

/// <summary>
/// Whether the two callees of a rebound pair, members of one assembly at two versions, run the same IL (ADR 0061; ticket
/// P2-179). Each member is read from the module its side's compilation references (<see cref="IlAstReader.Library"/>), and
/// the two are compared instruction by instruction, a token by what it names. A method of the assembly that the bodies reach
/// is compared the same way in turn, and a pair already met counts as the same, so a cycle is ADR 0019's mutual-summary
/// rule. A member of another assembly is the same when it has the same name and is a framework member no runtime-changes
/// row names, or one of the same build on both sides. Anything else is not the same, and the pair stays rebound (ADR 0042).
/// Each rebound pair is decided once per pair of compilations.
/// </summary>
internal sealed class TwoVersions
{
    /// <summary>The most member pairs compared for one rebound pair (ADR 0061 rule 6).</summary>
    public const int Bound = 256;

    private const string ReferenceAssemblyAttribute = "System.Runtime.CompilerServices.ReferenceAssemblyAttribute";

    /// <summary>The opcodes whose operand is a metadata token (ECMA-335 partition III), compared by what the token names.</summary>
    private static readonly FrozenSet<ILOpCode> Tokens = new[]
    {
        ILOpCode.Jmp, ILOpCode.Call, ILOpCode.Calli, ILOpCode.Callvirt, ILOpCode.Cpobj, ILOpCode.Ldobj, ILOpCode.Ldstr,
        ILOpCode.Newobj, ILOpCode.Castclass, ILOpCode.Isinst, ILOpCode.Unbox, ILOpCode.Ldfld, ILOpCode.Ldflda, ILOpCode.Stfld,
        ILOpCode.Ldsfld, ILOpCode.Ldsflda, ILOpCode.Stsfld, ILOpCode.Stobj, ILOpCode.Box, ILOpCode.Newarr, ILOpCode.Ldelema,
        ILOpCode.Ldelem, ILOpCode.Stelem, ILOpCode.Unbox_any, ILOpCode.Refanyval, ILOpCode.Mkrefany, ILOpCode.Ldtoken,
        ILOpCode.Ldftn, ILOpCode.Ldvirtftn, ILOpCode.Initobj, ILOpCode.Constrained, ILOpCode.Sizeof,
    }.ToFrozenSet();

    /// <summary>
    /// The size of every other opcode's immediate operand in bytes, compared as raw bytes: a branch offset is relative, so
    /// equal instructions before it make equal targets. An opcode not listed has none; <c>switch</c> is read apart.
    /// </summary>
    private static readonly FrozenDictionary<ILOpCode, int> Immediates = new Dictionary<ILOpCode, int>
    {
        [ILOpCode.Ldarg_s] = 1,
        [ILOpCode.Ldarga_s] = 1,
        [ILOpCode.Starg_s] = 1,
        [ILOpCode.Ldloc_s] = 1,
        [ILOpCode.Ldloca_s] = 1,
        [ILOpCode.Stloc_s] = 1,
        [ILOpCode.Ldc_i4_s] = 1,
        [ILOpCode.Unaligned] = 1,
        [ILOpCode.Br_s] = 1,
        [ILOpCode.Brfalse_s] = 1,
        [ILOpCode.Brtrue_s] = 1,
        [ILOpCode.Beq_s] = 1,
        [ILOpCode.Bge_s] = 1,
        [ILOpCode.Bgt_s] = 1,
        [ILOpCode.Ble_s] = 1,
        [ILOpCode.Blt_s] = 1,
        [ILOpCode.Bne_un_s] = 1,
        [ILOpCode.Bge_un_s] = 1,
        [ILOpCode.Bgt_un_s] = 1,
        [ILOpCode.Ble_un_s] = 1,
        [ILOpCode.Blt_un_s] = 1,
        [ILOpCode.Leave_s] = 1,
        [ILOpCode.Ldarg] = 2,
        [ILOpCode.Ldarga] = 2,
        [ILOpCode.Starg] = 2,
        [ILOpCode.Ldloc] = 2,
        [ILOpCode.Ldloca] = 2,
        [ILOpCode.Stloc] = 2,
        [ILOpCode.Ldc_i4] = 4,
        [ILOpCode.Ldc_r4] = 4,
        [ILOpCode.Br] = 4,
        [ILOpCode.Brfalse] = 4,
        [ILOpCode.Brtrue] = 4,
        [ILOpCode.Beq] = 4,
        [ILOpCode.Bge] = 4,
        [ILOpCode.Bgt] = 4,
        [ILOpCode.Ble] = 4,
        [ILOpCode.Blt] = 4,
        [ILOpCode.Bne_un] = 4,
        [ILOpCode.Bge_un] = 4,
        [ILOpCode.Bgt_un] = 4,
        [ILOpCode.Ble_un] = 4,
        [ILOpCode.Blt_un] = 4,
        [ILOpCode.Leave] = 4,
        [ILOpCode.Ldc_i8] = 8,
        [ILOpCode.Ldc_r8] = 8,
    }.ToFrozenDictionary();

    /// <summary>
    /// Each runtime-changes row's member up to and with the parenthesis that opens its parameter list, if it has one: a
    /// callee whose name followed by a parenthesis starts with one is never the same, whatever its parameters. A row that
    /// names a type, or the start of a member's name, so covers every member it starts.
    /// </summary>
    private static readonly ImmutableArray<string> Rows =
        [.. RuntimeChangeTable.Load().Rows.Select(static r => r.Member).Select(static m => m.Contains('(', StringComparison.Ordinal) ? m[..(m.IndexOf('(', StringComparison.Ordinal) + 1)] : m).Distinct(StringComparer.Ordinal)];

    private readonly Dictionary<(Compilation Legacy, Compilation Modern, string L, string M), bool> decided = [];

    /// <summary>
    /// Whether <paramref name="legacy"/>, called in <paramref name="legacyCompilation"/>, and <paramref name="modern"/>,
    /// called in <paramref name="modernCompilation"/>, are one function by ADR 0061. False when either is unknown.
    /// </summary>
    public bool Same(IMethodSymbol? legacy, Compilation legacyCompilation, IMethodSymbol? modern, Compilation modernCompilation)
    {
        if (legacy is null || modern is null)
        {
            return false;
        }

        (Compilation, Compilation, string, string) key = (legacyCompilation, modernCompilation, Key(legacy), Key(modern));
        if (!decided.TryGetValue(key, out bool same))
        {
            same = Compare(legacy, legacyCompilation, modern, modernCompilation);
            decided[key] = same;
        }

        return same;
    }

    private static string Key(IMethodSymbol method) => CallIdentityFactory.Name(method, RenameMap.Empty);

    /// <summary>
    /// Rules 1 and 2 on the two symbols: one assembly name, each call running the member's own body, the same type arguments
    /// at the call; then both definitions, read from their modules, compared with everything they reach.
    /// </summary>
    private static bool Compare(IMethodSymbol legacy, Compilation legacyCompilation, IMethodSymbol modern, Compilation modernCompilation) =>
        string.Equals(legacy.ContainingAssembly.Identity.Name, modern.ContainingAssembly.Identity.Name, StringComparison.OrdinalIgnoreCase)
        && Direct(legacy)
        && Direct(modern)
        && TypeArguments(legacy).SequenceEqual(TypeArguments(modern), StringComparer.Ordinal)
        && Definition(legacy, legacyCompilation) is { } l
        && Definition(modern, modernCompilation) is { } m
        && new Walk((MetadataModule)l.ParentModule!, (MetadataModule)m.ParentModule!).Same(l, m);

    /// <summary>A call to <paramref name="method"/> runs its own body: it is not virtual, abstract or an override, sealed or not.</summary>
    private static bool Direct(IMethodSymbol method) => !(method.IsVirtual | method.IsAbstract | method.IsOverride); // NOSONAR: one test of three flags

    private static IEnumerable<string> TypeArguments(IMethodSymbol method) =>
        Outermost(method.ContainingType).SelectMany(static t => t.TypeArguments).Concat(method.TypeArguments)
            .Select(static t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    private static IEnumerable<INamedTypeSymbol> Outermost(INamedTypeSymbol type) =>
        type.ContainingType is { } outer ? Outermost(outer).Append(type) : [type];

    /// <summary><paramref name="method"/>'s definition in the module its compilation references, or null when there is none to read.</summary>
    private static IlMethod? Definition(IMethodSymbol method, Compilation compilation) =>
        IlAstReader.Library(method.ContainingAssembly, compilation) is { } module
            ? IdStringProvider.FindEntity(method.OriginalDefinition.GetDocumentationCommentId()!, new SimpleTypeResolveContext(module)) as IlMethod
            : null;

    /// <summary>
    /// A type's name as both builds spell it, whatever assembly holds it: a type parameter by its owner and position, a
    /// constructed type by its definition and arguments, a type the runtime defines (<c>native int</c>) by its own name. A type
    /// that did not resolve, and any other kind (a pointer, a function pointer, a modified type), gets a name no other type
    /// has, so it is never the same as anything.
    /// </summary>
    private static string Name(IlType type) => type switch
    {
        { Kind: IlTypeKind.Unknown } => $"?{Guid.NewGuid():N}",
        IlSpecialType special => special.ReflectionName,
        ITypeParameter parameter => (parameter.OwnerType == SymbolKind.Method ? "!!" : "!") + parameter.Index,
        ParameterizedType generic => $"{Name(generic.GenericType)}<{string.Join(',', generic.TypeArguments.Select(Name))}>",
        ArrayType array => $"{Name(array.ElementType)}[{new string(',', array.Dimensions - 1)}]",
        ByReferenceType reference => Name(reference.ElementType) + "&",
        ITypeDefinition definition => definition.ReflectionName,
        _ => $"?{Guid.NewGuid():N}",
    };

    /// <summary>The type definitions <paramref name="type"/> is built from, in the order <see cref="Name"/> spells them.</summary>
    private static IEnumerable<ITypeDefinition> Definitions(IlType type) => type switch
    {
        ParameterizedType generic => [.. Definitions(generic.GenericType), .. generic.TypeArguments.SelectMany(Definitions)],
        TypeWithElementType element => Definitions(element.ElementType),
        ITypeDefinition definition => [definition],
        _ => [],
    };

    /// <summary>A member a call may dispatch away from: virtual, abstract or an override, sealed or not.</summary>
    private static bool Virtual(IMember member) => member.IsVirtual | member.IsAbstract | member.IsOverride; // NOSONAR: one test of three flags

    /// <summary>A framework module: a reference assembly the project compiled against, which the runtime replaces.</summary>
    private static bool Framework(MetadataModule module) =>
        module.GetAssemblyAttributes().Any(static a => string.Equals(a.AttributeType.FullName, ReferenceAssemblyAttribute, StringComparison.Ordinal));

    private static Guid Build(MetadataModule module) => module.MetadataFile.Metadata.GetGuid(module.MetadataFile.Metadata.GetModuleDefinition().Mvid);

    /// <summary>Whether a runtime-changes row names <paramref name="member"/>, any overload of it, or its type, at any runtime.</summary>
    private static bool RuntimeChanged(IMember member)
    {
        string name = $"{member.DeclaringTypeDefinition!.ReflectionName.Replace('+', '.')}::{member.Name}(";
        return Rows.Any(row => name.StartsWith(row, StringComparison.Ordinal));
    }

    /// <summary>One side of a comparison: the module the rebound callee is read from, and the names of its virtual methods.</summary>
    private sealed class Side(MetadataModule module)
    {
        private readonly Lazy<FrozenSet<string>> overridable = new(() => Overridable(module.MetadataFile.Metadata));

        public MetadataReader Metadata => module.MetadataFile.Metadata;

        /// <summary>Whether <paramref name="name"/> is the name of a virtual method of the module, which may override a framework one.</summary>
        public bool Overrides(string name) => overridable.Value.Contains(name);

        public bool Inside(IEntity entity) => entity.ParentModule == module;

        /// <summary><paramref name="method"/>'s implementation flags and body, or null when it has none: abstract, <c>extern</c> or runtime-implemented.</summary>
        public (MethodImplAttributes Flags, MethodBodyBlock Body)? Read(IlMethod method)
        {
            MethodDefinition definition = Metadata.GetMethodDefinition((MethodDefinitionHandle)method.MetadataToken);
            return definition.RelativeVirtualAddress == 0 ? null : (definition.ImplAttributes, module.MetadataFile.GetMethodBody(definition.RelativeVirtualAddress));
        }

        public bool HasInitialData(IField field) =>
            (Metadata.GetFieldDefinition((FieldDefinitionHandle)field.MetadataToken).Attributes & FieldAttributes.HasFieldRVA) == FieldAttributes.HasFieldRVA;

        public TypeAttributes Attributes(ITypeDefinition type) => Metadata.GetTypeDefinition((TypeDefinitionHandle)type.MetadataToken).Attributes;

        /// <summary>What a token names, read in <paramref name="context"/>: a type, a method, a field, a string, or null for anything else.</summary>
        public object? Resolve(int token, GenericContext context)
        {
            Handle handle = MetadataTokens.Handle(token);
            return handle.Kind switch
            {
                HandleKind.UserString => Metadata.GetUserString((UserStringHandle)handle),
                HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification => module.ResolveType((EntityHandle)handle, context),
                HandleKind.MethodDefinition or HandleKind.MethodSpecification => module.ResolveMethod((EntityHandle)handle, context),
                HandleKind.MemberReference or HandleKind.FieldDefinition => module.ResolveEntity((EntityHandle)handle, context),
                _ => null,
            };
        }

        public ImmutableArray<IlType> Locals(MethodBodyBlock body, GenericContext context) =>
            body.LocalSignature.IsNil ? [] : module.DecodeLocalSignature(body.LocalSignature, context);

        private static FrozenSet<string> Overridable(MetadataReader metadata) =>
            metadata.MethodDefinitions
                .Select(metadata.GetMethodDefinition)
                .Where(static m => (m.Attributes & MethodAttributes.Virtual) == MethodAttributes.Virtual)
                .Select(m => metadata.GetString(m.Name).Split('.')[^1])
                .ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>What two equal bodies share besides their types, regions and instructions, compared at once.</summary>
    private readonly record struct Header(bool IsStatic, int TypeParameters, MethodImplAttributes Flags, bool InitLocals, int Length)
    {
        public static Header Of(IlMethod method, (MethodImplAttributes Flags, MethodBodyBlock Body) read) =>
            new(method.IsStatic, method.TypeParameters.Count, read.Flags, read.Body.LocalVariablesInitialized, read.Body.GetILReader().Length);
    }

    /// <summary>An exception region: its kind, its offsets and the name of the type it catches.</summary>
    private readonly record struct Region(ExceptionRegionKind Kind, int TryOffset, int TryLength, int HandlerOffset, int HandlerLength, int FilterOffset, string? Catches);

    /// <summary>
    /// One comparison: the member pairs met so far, each compared once from a queue, and the type pairs whose shape was
    /// compared. A pair met again is assumed the same; the answer is that every pair met was.
    /// </summary>
    private sealed class Walk(MetadataModule legacyModule, MetadataModule modernModule)
    {
        private readonly Side legacy = new(legacyModule);
        private readonly Side modern = new(modernModule);
        private readonly HashSet<(int Legacy, int Modern)> met = [];
        private readonly Queue<(IlMethod Legacy, IlMethod Modern)> pending = [];
        private readonly HashSet<(int Legacy, int Modern)> shaped = [];

        public bool Same(IlMethod l, IlMethod m)
        {
            bool same = Meet(l, m);
            while (same && pending.TryDequeue(out (IlMethod Legacy, IlMethod Modern) next))
            {
                same = Body(next.Legacy, next.Modern);
            }

            return same;
        }

        private bool Meet(IlMethod l, IlMethod m)
        {
            if (met.Add((MetadataTokens.GetToken(l.MetadataToken), MetadataTokens.GetToken(m.MetadataToken))))
            {
                pending.Enqueue((l, m));
            }

            return met.Count <= Bound;
        }

        /// <summary>Rules 2 and 3 on two definitions of the assembly: the same signature, flags, locals, regions and instructions.</summary>
        private bool Body(IlMethod l, IlMethod m)
        {
            if (l.DeclaringTypeDefinition!.IsReferenceType is false || m.DeclaringTypeDefinition!.IsReferenceType is false
                || legacy.Read(l) is not { } lb || modern.Read(m) is not { } mb)
            {
                return false;
            }

            GenericContext lc = new(l.DeclaringTypeDefinition.TypeParameters, l.TypeParameters);
            GenericContext mc = new(m.DeclaringTypeDefinition.TypeParameters, m.TypeParameters);
            return Header.Of(l, lb) == Header.Of(m, mb)
                && SameTypes([l.ReturnType, .. l.Parameters.Select(static p => p.Type)], [m.ReturnType, .. m.Parameters.Select(static p => p.Type)])
                && SameTypes(legacy.Locals(lb.Body, lc), modern.Locals(mb.Body, mc))
                && Regions(lb.Body, lc, mb.Body, mc)
                && Instructions(lb.Body.GetILReader(), lc, mb.Body.GetILReader(), mc)
                && StaticConstructors(l.DeclaringTypeDefinition, m.DeclaringTypeDefinition);
        }

        private bool Regions(MethodBodyBlock l, GenericContext lc, MethodBodyBlock m, GenericContext mc)
        {
            ImmutableArray<(Region Region, IlType? Catches)> lr = [.. l.ExceptionRegions.Select(r => Region(legacy, r, lc))];
            ImmutableArray<(Region Region, IlType? Catches)> mr = [.. m.ExceptionRegions.Select(r => Region(modern, r, mc))];
            return lr.Select(static r => r.Region).SequenceEqual(mr.Select(static r => r.Region))
                && lr.Zip(mr).All(p => p.First.Catches is null || SameType(p.First.Catches, p.Second.Catches!));
        }

        private static (Region Region, IlType? Catches) Region(Side side, ExceptionRegion region, GenericContext context)
        {
            IlType? catches = region.Kind == ExceptionRegionKind.Catch ? (IlType)side.Resolve(MetadataTokens.GetToken(region.CatchType), context)! : null;
            return (new Region(region.Kind, region.TryOffset, region.TryLength, region.HandlerOffset, region.HandlerLength, region.FilterOffset, catches?.ReflectionName), catches);
        }

        /// <summary>The two instruction streams, of equal length, in step: equal opcodes, equal immediates, tokens naming the same.</summary>
        private bool Instructions(BlobReader l, GenericContext lc, BlobReader m, GenericContext mc)
        {
            while (l.RemainingBytes > 0)
            {
                ILOpCode op = OpCode(ref l);
                if (op != OpCode(ref m) || !(Tokens.Contains(op) ? Token(op, legacy.Resolve(l.ReadInt32(), lc), modern.Resolve(m.ReadInt32(), mc)) : Raw(op, ref l, ref m)))
                {
                    return false;
                }
            }

            return true;
        }

        private static ILOpCode OpCode(ref BlobReader reader)
        {
            byte first = reader.ReadByte();
            return (ILOpCode)(first == 0xFE ? 0xFE00 | reader.ReadByte() : first);
        }

        /// <summary>An immediate operand, the same bytes on both sides; a <c>switch</c>'s is its count and that many offsets.</summary>
        private static bool Raw(ILOpCode op, ref BlobReader l, ref BlobReader m)
        {
            BlobReader peek = l;
            int size = op == ILOpCode.Switch ? 4 + (4 * peek.ReadInt32()) : Immediates.GetValueOrDefault(op);
            return l.ReadBytes(size).AsSpan().SequenceEqual(m.ReadBytes(size));
        }

        private bool Token(ILOpCode op, object? l, object? m) => (l, m) switch
        {
            (string ls, string ms) => string.Equals(ls, ms, StringComparison.Ordinal),
            (IlType lt, IlType mt) => SameType(lt, mt),
            (IlMethod lm, IlMethod mm) => SameMethod(op is ILOpCode.Callvirt or ILOpCode.Ldvirtftn, lm, mm),
            (IField lf, IField mf) => SameField(lf, mf),
            _ => false,
        };

        /// <summary>
        /// Rule 4 for a method of the assembly: called non-virtually, with the same type arguments, and itself the same; its
        /// type's static constructor runs first. Rule 5 for any other: the same name, from a framework module or the same
        /// build, no runtime-changes row naming it, and not a virtual call a method of either version may override.
        /// </summary>
        private bool SameMethod(bool dispatched, IlMethod l, IlMethod m)
        {
            bool inside = legacy.Inside(l);
            if (inside != modern.Inside(m))
            {
                return false;
            }

            bool virtualCall = dispatched && Virtual(l);
            return inside
                ? !virtualCall
                    && SameTypes([.. l.DeclaringType.TypeArguments, .. l.TypeArguments], [.. m.DeclaringType.TypeArguments, .. m.TypeArguments])
                    && Meet((IlMethod)l.MemberDefinition, (IlMethod)m.MemberDefinition)
                : string.Equals(l.Name, m.Name, StringComparison.Ordinal)
                    && SameTypes([l.DeclaringType, .. l.TypeArguments, l.ReturnType, .. l.Parameters.Select(static p => p.Type)], [m.DeclaringType, .. m.TypeArguments, m.ReturnType, .. m.Parameters.Select(static p => p.Type)])
                    && !RuntimeChanged(l)
                    && !(virtualCall && (legacy.Overrides(l.Name) || modern.Overrides(l.Name)));
        }

        /// <summary>A field: the same name, type and declaring type; one of the assembly holds no initial data, and a static one's type's static constructor is the same.</summary>
        private bool SameField(IField l, IField m) =>
            string.Equals(l.Name, m.Name, StringComparison.Ordinal)
            && SameTypes([l.DeclaringType, l.Type], [m.DeclaringType, m.Type])
            && (!legacy.Inside(l)
                || (!(legacy.HasInitialData((IField)l.MemberDefinition) | modern.HasInitialData((IField)m.MemberDefinition)) // NOSONAR: both sides are always asked
                    && (!l.IsStatic || StaticConstructors(l.DeclaringTypeDefinition!, m.DeclaringTypeDefinition!))));

        /// <summary>Both types have a static constructor that is the same, or neither has one.</summary>
        private bool StaticConstructors(ITypeDefinition l, ITypeDefinition m)
        {
            IlMethod? lc = l.Methods.FirstOrDefault(static c => c.IsConstructor && c.IsStatic);
            IlMethod? mc = m.Methods.FirstOrDefault(static c => c.IsConstructor && c.IsStatic);
            return lc is null ? mc is null : mc is not null && Meet(lc, mc);
        }

        private bool SameTypes(IReadOnlyList<IlType> l, IReadOnlyList<IlType> m) =>
            l.Count == m.Count && l.Zip(m).All(p => SameType(p.First, p.Second));

        /// <summary>The same name, and each type definition it is built from the same: rule 4 inside the assembly, rule 5 outside it.</summary>
        private bool SameType(IlType l, IlType m) =>
            string.Equals(Name(l), Name(m), StringComparison.Ordinal)
            && Definitions(l).Zip(Definitions(m)).All(p => SameDefinition(p.First, p.Second));

        private bool SameDefinition(ITypeDefinition l, ITypeDefinition m)
        {
            bool inside = legacy.Inside(l);
            return inside == modern.Inside(m) && (inside ? Shape(l, m) : Outside((MetadataModule)l.ParentModule!, (MetadataModule)m.ParentModule!));
        }

        /// <summary>A type of the assembly: not a value type, with the same flags, base type and interfaces on both sides.</summary>
        private bool Shape(ITypeDefinition l, ITypeDefinition m) =>
            !shaped.Add((MetadataTokens.GetToken(l.MetadataToken), MetadataTokens.GetToken(m.MetadataToken)))
            || (l.IsReferenceType is true
                && legacy.Attributes(l) == modern.Attributes(m)
                && SameTypes([.. l.DirectBaseTypes.OrderBy(Name, StringComparer.Ordinal)], [.. m.DirectBaseTypes.OrderBy(Name, StringComparer.Ordinal)]));

        /// <summary>A module other than the assembly: a framework one on both sides, or the same build.</summary>
        private static bool Outside(MetadataModule l, MetadataModule m) =>
            (Framework(l) & Framework(m)) || Build(l) == Build(m); // NOSONAR: both sides are always asked, one branch for the pair
    }
}
