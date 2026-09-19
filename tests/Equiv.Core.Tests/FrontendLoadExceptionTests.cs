using Xunit;

namespace Equiv.Core.Tests;

public sealed class FrontendLoadExceptionTests
{
    [Fact]
    public void StandardConstructorsSetTheExpectedProperties()
    {
        InvalidOperationException inner = new("inner");
        Assert.Null(new FrontendLoadException().InnerException);
        Assert.Equal("m", new FrontendLoadException("m").Message);
        Assert.Same(inner, new FrontendLoadException("m", inner).InnerException);
    }

    [Fact]
    public void PathAndDetailConstructorSetsPropertiesAndMessage()
    {
        FrontendLoadException exception = new("legacy.sln", "workspace diagnostic: MSB1234");

        Assert.Equal("legacy.sln", exception.Path);
        Assert.Equal("workspace diagnostic: MSB1234", exception.Detail);
        Assert.Equal("failed to load 'legacy.sln': workspace diagnostic: MSB1234", exception.Message);
    }
}
