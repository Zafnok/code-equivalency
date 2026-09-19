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

    private static ITypeSymbol TypeOf(string type)
    {
        Compilation compilation = RoslynTestCompilations.Compile(
            $"namespace N {{ class Outer {{ public class Inner {{ }} }} }}\nclass G {{ }}\nclass Holder {{ public {type} F; }}");
        return compilation.GetTypeByMetadataName("Holder")!.GetMembers("F").OfType<IFieldSymbol>().Single().Type;
    }
}
