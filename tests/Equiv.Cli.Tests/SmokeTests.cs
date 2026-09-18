using Xunit;

namespace Equiv.Cli.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void MainWithNoArgsReturnsZero()
    {
        int exitCode = Program.Main([]);

        Assert.Equal(0, exitCode);
    }
}
