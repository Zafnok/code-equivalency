using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>Lowers one method of a snippet and checks that the result validates (ticket M2-003: the validator runs in every test).</summary>
internal static class Lowered
{
    /// <summary>Lowers method <paramref name="name"/> of <c>class C { <paramref name="members"/> }</c>.</summary>
    public static IrProcedure Method(string members, string name = "M", bool allowErrors = false) =>
        Source($"using System;\nclass C\n{{\n{members}\n}}\n", name, allowErrors);

    /// <summary>Lowers the method named <paramref name="name"/> (metadata name: <c>.ctor</c>, <c>get_P</c>) of class <c>C</c> in a whole compilation unit.</summary>
    public static IrProcedure Source(string source, string name = "M", bool allowErrors = false, RenameMap? renames = null)
    {
        Compilation compilation = RoslynTestCompilations.Compile(source);
        if (!allowErrors)
        {
            Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        }

        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers(name).OfType<IMethodSymbol>().Single();
        IrProcedure procedure = IrLowerer.Lower(method, compilation, renames ?? RenameMap.Empty);
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    public static IrOutcome Run(IrProcedure procedure, params IrValue[] arguments) =>
        IrInterpreter.Run(procedure, new IrInputs([.. arguments]), IrGenOracle.Instance, IrGen.StepBudget).Outcome;

    public static ImmutableArray<IrOpaque> Opaques(IrProcedure procedure) =>
        [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>()];

    public static ImmutableArray<IrCall> Calls(IrProcedure procedure) =>
        [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>()];

    public static IrBitVecValue Bits(int width, long value) => IrBitVecValue.FromSigned(width, value);

    /// <summary>An element of an uninterpreted reference sort; only its identity matters.</summary>
    public static IrSortValue Reference(int id, string sort = "System.String") => new(sort, id);

    /// <summary>An empty <c>field.&lt;Type&gt;.&lt;Field&gt;</c> input over receivers of <paramref name="sort"/>.</summary>
    public static IrMapValue Fields(string sort, IrType value) => Empty(new IrMap(new IrSort(sort), value));

    /// <summary>An empty <c>array.&lt;v&gt;</c> input.</summary>
    public static IrMapValue Elements(IrType element) => Empty(new IrMap(new IrBitVec(32), element));

    /// <summary>A <c>null.&lt;Sort&gt;</c> input that answers <paramref name="isNull"/> for <paramref name="id"/> and false elsewhere.</summary>
    private static IrMapValue Empty(IrMap type) => new(
        type,
        type.Value is IrBool ? new IrBoolValue(false) : new IrBitVecValue(((IrBitVec)type.Value).Width, 0),
        ImmutableDictionary<IrValue, IrValue>.Empty);

    public static IrMapValue Nulls(string sort, int id, bool isNull) => new(
        new IrMap(new IrSort(sort), new IrBool()),
        new IrBoolValue(false),
        ImmutableDictionary<IrValue, IrValue>.Empty.Add(new IrSortValue(sort, id), new IrBoolValue(isNull)));
}
