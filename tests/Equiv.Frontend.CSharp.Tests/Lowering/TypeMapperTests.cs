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

    /// <summary>
    /// Ticket P2-032: a nullable reference annotation, on an array, its element or a type parameter, is not part of the
    /// sort, as it already is not for a named type; null tracking is the shadow's job, not the sort's.
    /// </summary>
    [Theory]
    [InlineData("string[]?", "string[]")]
    [InlineData("string?[]?", "string[]")]
    [InlineData("N.Outer.Inner[]?", "N.Outer.Inner[]")]
    [InlineData("System.Collections.Generic.List<string?>[]?", "System.Collections.Generic.List<string>[]")]
    [InlineData("string?", "System.String")]
    [InlineData("T?", "T")]
    public void ANullableAnnotationIsNotPartOfTheSort(string type, string sort) =>
        Assert.Equal(new IrSort(sort), TypeMapper.Map(TypeOf("#nullable enable\n", type)));

    /// <summary>
    /// Ticket P2-125: a tuple's element names are not part of the sort of a type that holds it, as they are not part of a
    /// named type's metadata name: an array of a tuple is one sort whatever its elements are called, and with no names.
    /// </summary>
    [Theory]
    [InlineData("(object First, string Second)[]", "(object, string)[]")]
    [InlineData("(object a, string b)[]", "(object, string)[]")]
    [InlineData("(object a, string b)[]", "(object First, string Second)[]")]
    [InlineData("(object a, (int x, string y) b)[][]", "(object, (int, string))[][]")]
    [InlineData("System.Collections.Generic.List<(object a, string b)>[]", "System.Collections.Generic.List<(object, string)>[]")]
    public void TupleArraysMapToOneTypeWhateverTheElementNames(string type, string other)
    {
        IrType mapped = TypeMapper.Map(TypeOf(type));

        Assert.IsType<IrSort>(mapped);
        Assert.Equal(TypeMapper.Map(TypeOf(other)), mapped);
    }

    /// <summary>Ticket P2-125: dropping the names does not merge arrays of tuples whose element types differ.</summary>
    [Fact]
    public void TupleArraysOfOtherElementTypesStayApart() =>
        Assert.NotEqual(TypeMapper.Map(TypeOf("(object a, string b)[]")), TypeMapper.Map(TypeOf("(string a, object b)[]")));

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
    [MemberData(nameof(Defaults), DisableDiscoveryEnumeration = true)]
    public void DefaultIsTheConstantOfADefault(string type, IrValue expected) =>
        Assert.Equal(expected, TypeMapper.Default(TypeOf(type), TypeMapper.Unmapped));

    /// <summary>
    /// ADR 0053 decision 5 (ticket P1-030): a <c>float</c> or <c>double</c> constant is the element whose id is its IEEE
    /// bits, whatever numeric type the constant's value arrives as, with one NaN and two zeros.
    /// </summary>
    [Fact]
    public void AFloatingPointConstantIsTheElementOfItsBits()
    {
        Assert.Equal(new IrSortValue("System.Double", 0x4000_0000_0000_0000), TypeMapper.Constant(TypeOf("double"), 2.0));
        Assert.Equal(new IrSortValue("System.Double", 0x4000_0000_0000_0000), TypeMapper.Constant(TypeOf("double"), 2));
        Assert.Equal(new IrSortValue("System.Single", 0x3F00_0000), TypeMapper.Constant(TypeOf("float"), 0.5f));
        Assert.Equal(new IrSortValue("System.Single", 0x4000_0000), TypeMapper.Constant(TypeOf("float"), 2.0));
        Assert.Equal(new IrSortValue("System.Double", 0), TypeMapper.Constant(TypeOf("double"), 0.0));
        Assert.Equal(new IrSortValue("System.Double", long.MinValue), TypeMapper.Constant(TypeOf("double"), -0.0));
        Assert.Equal(IrFloat.Of(double.NaN), TypeMapper.Constant(TypeOf("double"), -double.NaN));
        Assert.Equal(IrFloat.Of(float.NaN), TypeMapper.Constant(TypeOf("float"), float.NaN));
        Assert.Equal(TypeMapper.Constant(TypeOf("double"), 0.1), TypeMapper.Constant(TypeOf("double"), 0.1));
        Assert.NotEqual(TypeMapper.Constant(TypeOf("double"), 0.1), TypeMapper.Constant(TypeOf("double"), 0.1f));
    }

    /// <summary>An API-equivalence adapter's floating-point constant is the element the C# constant with that text is.</summary>
    [Theory]
    [InlineData("System.Double", "1.5", 1.5)]
    [InlineData("System.Double", "-0", -0.0)]
    [InlineData("System.Double", "1E+300", 1e300)]
    [InlineData("System.Double", "NaN", double.NaN)]
    public void AnAdapterFloatingPointConstantIsItsNumber(string type, string text, double value)
    {
        Assert.Equal(IrFloat.Of(value), TypeMapper.Constant(type, text));
        Assert.Equal(TypeMapper.Constant(TypeOf("double"), value), TypeMapper.Constant(type, text));
    }

    [Fact]
    public void AnAdapterConstantOfAnotherSortOrOfNoNumberIsAsBefore()
    {
        Assert.Equal(IrFloat.Of(1.5f), TypeMapper.Constant("System.Single", "1.5"));
        Assert.Equal(TypeMapper.Constant(TypeOf("float"), 1.5f), TypeMapper.Constant("System.Single", "1.5"));
        Assert.Null(TypeMapper.Constant("System.Single", "one"));
        Assert.Null(TypeMapper.Constant("System.Double", "one"));
        Assert.Null(TypeMapper.Constant("System.Double", "1,5"));
        Assert.Equal(TypeMapper.Constant(TypeOf("string"), "1.5"), TypeMapper.Constant("System.String", "1.5"));
        Assert.Equal(TypeMapper.Constant(TypeOf("decimal"), 1.5m), TypeMapper.Constant("System.Decimal", "1.5"));
    }

    /// <summary>Ticket P2-095: the <c>Nullable&lt;T&gt;</c> whose conversion from <c>T</c> lowers are those of a <c>bool</c> or integral <c>T</c>.</summary>
    [Theory]
    [InlineData("int?", "int")]
    [InlineData("System.Nullable<byte>", "byte")]
    [InlineData("ulong?", "ulong")]
    [InlineData("char?", "char")]
    [InlineData("bool?", "bool")]
    public void ANullableOfABoolOrIntegralTypeHasItsValueType(string type, string underlying)
    {
        Assert.Equal(TypeOf(underlying), TypeMapper.NullableValue(TypeOf(type)), SymbolEqualityComparer.Default);
        Assert.True(TypeMapper.IsNullable(TypeOf(type)));
        Assert.Equal(new IrSort("System.Nullable`1"), TypeMapper.Map(TypeOf(type)));
    }

    [Theory]
    [InlineData("double?", true)]
    [InlineData("System.DayOfWeek?", true)]
    [InlineData("System.DateTime?", true)]
    [InlineData("int", false)]
    [InlineData("string", false)]
    public void AnyOtherTypeHasNoNullableValueType(string type, bool isNullable)
    {
        Assert.Null(TypeMapper.NullableValue(TypeOf(type)));
        Assert.Equal(isNullable, TypeMapper.IsNullable(TypeOf(type)));
        Assert.Null(TypeMapper.NullableValue(type: null));
        Assert.False(TypeMapper.IsNullable(type: null));
    }

    [Fact]
    public void AStructHasNoConstantDefault() => Assert.Null(TypeMapper.Default(TypeOf("System.DateTime"), TypeMapper.Unmapped));

    private static ITypeSymbol TypeOf(string type) => TypeOf(string.Empty, type);

    private static ITypeSymbol TypeOf(string prefix, string type)
    {
        Compilation compilation = RoslynTestCompilations.Compile(
            $"{prefix}namespace N {{ class Outer {{ public class Inner {{ }} }} }}\nclass G {{ }}\nclass Holder<T> {{ public {type} F = default!; }}");
        return compilation.GetTypeByMetadataName("Holder`1")!.GetMembers("F").OfType<IFieldSymbol>().Single().Type;
    }
}
