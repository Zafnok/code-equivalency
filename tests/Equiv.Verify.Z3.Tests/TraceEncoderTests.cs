using Equiv.Core;
using Equiv.Core.Ir;

using Microsoft.Z3;

using Xunit;

using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3.Tests;

/// <summary>The Z3 function names <see cref="TraceEncoder"/> mints for a callee, and their caching.</summary>
public sealed class TraceEncoderTests
{
    [Fact]
    public void RuntimeChangedFunctionsCarryASidePrefixedOwnerTag()
    {
        using Context context = new();
        TraceEncoder encoder = new(new SortMapper(context), [], [], []);
        CallIdentity callee = new("Svc::F(int)", RuntimeChanged: true);

        FuncDecl old = encoder.ResultFunction(Side.Old, callee, [], new IrBitVec(32));
        FuncDecl @new = encoder.ResultFunction(Side.New, callee, [], new IrBitVec(32));

        Assert.Equal("f:Svc::F(int)()->bv32:old", old.Name.ToString());
        Assert.Equal("f:Svc::F(int)()->bv32:new", @new.Name.ToString());
    }

    [Fact]
    public void TheSameCalleeAndSignatureReuseTheSameFunctionDeclaration()
    {
        using Context context = new();
        TraceEncoder encoder = new(new SortMapper(context), [], [], []);
        CallIdentity callee = new("Svc::F(int)");

        FuncDecl first = encoder.ResultFunction(Side.Old, callee, [], new IrBitVec(32));
        FuncDecl second = encoder.ResultFunction(Side.Old, callee, [], new IrBitVec(32));

        Assert.Same(first, second);
    }
}
