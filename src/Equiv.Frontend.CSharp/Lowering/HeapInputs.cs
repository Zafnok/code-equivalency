using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The synthesised inputs a lowered body needs beyond its C# parameters (VERIFICATION-MODEL.md
/// section 2; ticket M2-004): the receiver <c>this</c> and one <c>null.&lt;Sort&gt;</c> map per
/// reference sort whose nullness is read. Each is created once, on first use, and they become <see cref="IrParameterKind.In"/> parameters ordered by name, so both
/// sides of a pair share them by name exactly as they share the C# parameters. IR variable names take
/// only letters, digits, <c>_</c>, <c>.</c> and <c>$</c>, so every part of a name is spelled with dots.
/// </summary>
internal sealed class HeapInputs
{
    private static readonly IrBool Bool = new();

    private readonly Dictionary<string, IrVar> inputs = new(StringComparer.Ordinal);

    /// <summary>The synthesised parameters, ordered by name.</summary>
    public ImmutableArray<IrParameter> Parameters =>
        [.. inputs.Values.OrderBy(static v => v.Name, StringComparer.Ordinal).Select(static v => new IrParameter(v, IrParameterKind.In))];

    /// <summary>The receiver of an instance method, as an uninterpreted value of its containing type.</summary>
    public IrVar This(INamedTypeSymbol type) => Input("this", new IrSort(TypeMapper.MetadataName(type)));

    /// <summary>Whether each value of <paramref name="sort"/> is null; equal references are equally null.</summary>
    public IrVar Nulls(IrSort sort) => Input($"null.{Part(sort.Name)}", new IrMap(sort, Bool));

    private static string Part(string name) =>
        string.Concat(name.Select(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' ? c : '_'));

    private IrVar Input(string name, IrType type)
    {
        if (!inputs.TryGetValue(name, out IrVar? input))
        {
            input = new IrVar(name, type);
            inputs[name] = input;
        }

        return input;
    }
}
