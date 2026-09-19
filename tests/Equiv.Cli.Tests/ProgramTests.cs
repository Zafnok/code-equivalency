using Xunit;

namespace Equiv.Cli.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void Main_WithNoArgsExits3()
    {
        int exitCode = Program.Main([]);

        Assert.Equal(ExitCodes.UsageError, exitCode);
    }

    [Fact]
    public void Main_WithValidArgsButNoConfiguredFrontendExits3()
    {
        using TempFile legacy = new();
        using TempFile modern = new();

        // Program.Main wires an empty frontend list until a real frontend ships (ticket goal:
        // "the shipped binary rejects every real input with exit 3"), so a well-formed parse still
        // ends in a router rejection. This also exercises the parseResult.Invoke() branch that
        // Main_WithNoArgsExits3's parse-error branch does not reach.
        int exitCode = Program.Main(["compare", "--legacy", legacy.Path, "--modern", modern.Path]);

        Assert.Equal(ExitCodes.UsageError, exitCode);
    }
}
