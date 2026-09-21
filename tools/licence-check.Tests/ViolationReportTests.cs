using LicenceCheck;

using Xunit;

namespace LicenceCheck.Tests;

public sealed class ViolationReportTests
{
    [Fact]
    public void FormatsAHeaderLineAndOneLinePerViolation()
    {
        LicenceViolation[] violations =
        [
            new("PackageA", "1.0.0", "licence 'GPL-3.0' is not on the allowlist and no policy exception permits it."),
            new("PackageB", "2.0.0", "licence could not be determined."),
        ];

        IReadOnlyList<string> lines = ViolationReport.FormatLines(violations);

        Assert.Equal(3, lines.Count);
        Assert.Equal("licence-check: 2 package(s) failed the dependency licence gate:", lines[0]);
        Assert.Equal("  - PackageA 1.0.0: licence 'GPL-3.0' is not on the allowlist and no policy exception permits it.", lines[1]);
        Assert.Equal("  - PackageB 2.0.0: licence could not be determined.", lines[2]);
    }

    [Fact]
    public void FormatsJustTheHeaderWhenThereAreNoViolations()
    {
        IReadOnlyList<string> lines = ViolationReport.FormatLines([]);

        string line = Assert.Single(lines);
        Assert.Equal("licence-check: 0 package(s) failed the dependency licence gate:", line);
    }
}
