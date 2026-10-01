using Xunit;

namespace Equiv.Execute.Tests;

/// <summary>Ticket P2-056 criterion 3: Windows is needed only when some project runs on .NET Framework.</summary>
public sealed class WindowsRequirementTests
{
    [Fact]
    public void OnlyAFrameworkProjectNeedsWindows()
    {
        (string, string)[] core = [("App", "net8.0"), ("Lib", "netstandard2.0"), ("Unknown", "unknown")];
        (string, string)[] hosted = [.. core, ("Shared", "net48, net8.0"), ("Old", "net472")];

        Assert.Null(WindowsRequirement.Refusal(isWindows: false, core));
        Assert.Null(WindowsRequirement.Refusal(isWindows: true, hosted));
        Assert.Equal("Shared runs on net48, and .NET Framework needs Windows (ADR 0040)", WindowsRequirement.Refusal(isWindows: false, hosted));
        Assert.Throws<ArgumentNullException>(() => WindowsRequirement.Refusal(isWindows: false, null!));
    }
}
