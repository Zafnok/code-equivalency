using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Core.Tests.Ir;

/// <summary>The naming rule of VERIFICATION-MODEL.md section 2 (ADR 0021; ticket M3-007 acceptance criterion 8).</summary>
public sealed class IrParameterNamesTests
{
    [Theory]
    [InlineData("a", false)]
    [InlineData("$this", false)]
    [InlineData("this", true)]
    [InlineData("field.C.x", true)]
    [InlineData("null.S", true)]
    [InlineData("array.int__", true)]
    [InlineData("length.int__", true)]
    public void SynthesisedInputsAreTheReceiverAndTheDottedNames(string name, bool synthesised) =>
        Assert.Equal(synthesised, IrParameterNames.IsSynthesised(name));

    [Fact]
    public void NullIsRejected() => Assert.Throws<ArgumentNullException>(static () => IrParameterNames.IsSynthesised(null!));
}
