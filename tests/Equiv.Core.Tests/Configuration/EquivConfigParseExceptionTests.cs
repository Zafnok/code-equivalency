using Equiv.Core.Configuration;

using Xunit;

namespace Equiv.Core.Tests.Configuration;

public sealed class EquivConfigParseExceptionTests
{
    [Fact]
    public void StandardConstructorsSetTheExpectedProperties()
    {
        InvalidOperationException inner = new("inner");
        Assert.Null(new EquivConfigParseException().InnerException);
        Assert.Equal("m", new EquivConfigParseException("m").Message);
        Assert.Same(inner, new EquivConfigParseException("m", inner).InnerException);
    }
}
