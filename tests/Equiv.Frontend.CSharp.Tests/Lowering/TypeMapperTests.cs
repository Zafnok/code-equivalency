using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>The M2-003 type map (ADR 0013 for <c>char</c>).</summary>
public sealed class TypeMapperTests
{
    [Theory]
    [InlineData("sbyte", 8, true)]
    [InlineData("byte", 8, false)]
    [InlineData("short", 16, true)]
    [InlineData("ushort", 16, false)]
    [InlineData("char", 16, false)]
    [InlineData("int", 32, true)]
    [InlineData("uint", 32, false)]
    [InlineData("long", 64, true)]
    [InlineData("ulong", 64, false)]
    public void IntegralTypesAreBitVectorsWithSignednessOnTheSide(string type, int width, bool isSigned)
    {
        ITypeSymbol symbol = TypeOf(type);

        Assert.Equal(new IrBitVec(width), TypeMapper.Map(symbol));
        Assert.Equal(isSigned, TypeMapper.IsSigned(symbol));
    }

    [Fact]
    public void BoolIsBool() => Assert.Equal(new IrBool(), TypeMapper.Map(TypeOf("bool")));

    [Theory]
    [InlineData("string", "System.String")]
    [InlineData("double", "System.Double")]
    [InlineData("N.Outer.Inner", "N.Outer+Inner")]
    [InlineData("G", "G")]
    [InlineData("int[]", "int[]")]
    [InlineData("System.Collections.Generic.List<int>", "System.Collections.Generic.List`1")]
    public void EverythingElseIsASortNamedByMetadataName(string type, string sort)
    {
        Assert.Equal(new IrSort(sort), TypeMapper.Map(TypeOf(type)));
        Assert.False(TypeMapper.IsSigned(TypeOf(type)));
    }

    /// <summary>Ticket P2-001: the element a new array starts with, which is the constant a literal default is.</summary>
    public static TheoryData<string, IrValue> Defaults() => new()
    {
        { "string", new IrSortValue("System.String", 0) },
        { "int?", new IrSortValue("System.Nullable`1", 0) },
        { "bool", new IrBoolValue(Value: false) },
        { "int", new IrBitVecValue(32, 0) },
        { "char", new IrBitVecValue(16, 0) },
        { "double", TypeMapper.Constant(TypeOf("double"), 0.0) },
        { "System.DayOfWeek", TypeMapper.Constant(TypeOf("System.DayOfWeek"), 0) },
    };

    [Theory]
    [MemberData(nameof(Defaults))]
    public void DefaultIsTheConstantOfADefault(string type, IrValue expected) =>
        Assert.Equal(expected, TypeMapper.Default(TypeOf(type), TypeMapper.Unmapped));

    [Fact]
    public void AStructHasNoConstantDefault() => Assert.Null(TypeMapper.Default(TypeOf("System.DateTime"), TypeMapper.Unmapped));

    private static ITypeSymbol TypeOf(string type)
    {
        Compilation compilation = RoslynTestCompilations.Compile(
            $"namespace N {{ class Outer {{ public class Inner {{ }} }} }}\nclass G {{ }}\nclass Holder {{ public {type} F; }}");
        return compilation.GetTypeByMetadataName("Holder")!.GetMembers("F").OfType<IFieldSymbol>().Single().Type;
    }
}
