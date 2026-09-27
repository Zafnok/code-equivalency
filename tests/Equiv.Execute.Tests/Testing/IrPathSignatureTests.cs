using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Execute.Testing;

using Xunit;

namespace Equiv.Execute.Tests.Testing;

public sealed class IrPathSignatureTests
{
    private static readonly IrMap Nulls = new(new IrSort("System.String"), new IrBool());

    [Fact]
    public void WireArgumentsBecomeTheBodysParametersByPosition()
    {
        IrProcedure body = IrText.Parse("""
            proc "T::M" (%b: bool, %n: bv8, %u: bv16, %s: sort "System.String", %t: sort "System.String", %u2: sort "System.String", %this: sort "T", %null.System.String: map<sort "System.String", bool>, %field.T.x: map<sort "T", bv32>, %m: map<bv8, bv8>, %missing: bv32) -> bool entry B0
            B0:
              ret %b
            """);

        IrInputs inputs = IrPathSignature.Inputs(body, new ExecutionInput(["true", "-1", "65535", "null", "\"a\"", "null", "7"]));

        IrSortValue nullString = new("System.String", 1);
        Assert.Equal<IrValue>(
            [
                new IrBoolValue(Value: true),
                new IrBitVecValue(8, 255),
                new IrBitVecValue(16, 65535),
                nullString,
                new IrSortValue("System.String", 2),
                nullString,
                new IrSortValue("T", 3),
                new IrMapValue(Nulls, new IrBoolValue(Value: false), ImmutableDictionary<IrValue, IrValue>.Empty.Add(nullString, new IrBoolValue(Value: true))),
                new IrMapValue(new IrMap(new IrSort("T"), new IrBitVec(32)), new IrBitVecValue(32, 0), ImmutableDictionary<IrValue, IrValue>.Empty),
                new IrMapValue(new IrMap(new IrBitVec(8), new IrBitVec(8)), new IrBitVecValue(8, 0), ImmutableDictionary<IrValue, IrValue>.Empty),
                new IrBitVecValue(32, 0),
            ],
            inputs.Arguments);
    }

    [Theory]
    [InlineData("18446744073709551615", 64, ulong.MaxValue)]
    [InlineData("-2", 32, 0xFFFFFFFEUL)]
    [InlineData("\"x\"", 32, 0UL)]
    public void IntegersWrapToTheirWidthAndANonIntegerIsZero(string argument, int width, ulong bits)
    {
        IrProcedure body = IrText.Parse(string.Create(CultureInfo.InvariantCulture, $$"""
            proc "T::M" (%a: bv{{width}}) -> bv{{width}} entry B0
            B0:
              ret %a
            """));

        Assert.Equal(new IrBitVecValue(width, bits), Assert.Single(IrPathSignature.Inputs(body, new ExecutionInput([argument])).Arguments));
    }

    [Fact]
    public void Of_ListsTheBlocksEnteredUntilABranchOnACall()
    {
        IrProcedure body = IrText.Parse("""
            proc "T::M" (%c: bool, %r: sort "T") -> bool entry B0
            B0:
              br %c, B1, B2
            B1:
              %t: bool = call "T::F"(%r)
              br %t, B2, B3
            B2:
              ret %c
            B3:
              ret %c
            """);

        Assert.Equal("0,1", IrPathSignature.Of(body, new ExecutionInput(["true", "null"])));
        Assert.Equal("0,2", IrPathSignature.Of(body, new ExecutionInput(["false", "null"])));
    }

    [Fact]
    public void DefaultOracle_AnswersEveryTypeWithItsDefault()
    {
        IrMap map = new(new IrBitVec(8), new IrSort("S"));

        IrCallResult call = DefaultOracle.Instance.Answer(new CallIdentity("T::F"), [], new IrBool(), 0, [], [new IrBitVec(16), map]);
        IrCallResult voidCall = DefaultOracle.Instance.Answer(new CallIdentity("T::F"), [], resultType: null, 0, [], []);
        IrPureResult pure = DefaultOracle.Instance.Answer(
            new IrPure(new IrVar("q", new IrSort("System.Decimal")), [new IrPureThrow(new IrVar("z", new IrBool()), "E")], "dec.div", []), []);

        Assert.Equal(new IrCallResult(new IrBoolValue(Value: false), Threw: false) { RefOuts = [new IrBitVecValue(16, 0), new IrMapValue(map, new IrSortValue("S", 0), [])] }, call);
        Assert.Null(voidCall.Value);
        Assert.Equal(new IrPureResult(new IrSortValue("System.Decimal", 0), [false]), pure);
    }
}
