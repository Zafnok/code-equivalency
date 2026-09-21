using LicenceCheck;

using Xunit;

namespace LicenceCheck.Tests;

public sealed class LicenceGateTests
{
    private static readonly LicencePolicy Policy = new(
        ["MIT", "Apache-2.0"],
        [new LicenceException("dotnet-sonarscanner", "LGPL-3.0", "CI process, never linked or shipped.")]);

    [Fact]
    public void AllowedLicencePasses()
    {
        ResolvedPackage package = new("Sarif.Sdk", "5.7.0", PackageRole.Redistributed, "MIT", IsSpdxExpression: true);

        LicenceGateResult result = LicenceGate.Evaluate([package], Policy);

        Assert.True(result.Success);
        Assert.Equal([package], result.Passed);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void DeniedLicenceFails()
    {
        ResolvedPackage package = new("SomeCopyleftThing", "1.0.0", PackageRole.Redistributed, "GPL-3.0", IsSpdxExpression: true);

        LicenceGateResult result = LicenceGate.Evaluate([package], Policy);

        Assert.False(result.Success);
        LicenceViolation violation = Assert.Single(result.Violations);
        Assert.Equal("SomeCopyleftThing", violation.PackageId);
        Assert.Equal("1.0.0", violation.Version);
        Assert.Contains("GPL-3.0", violation.Message, StringComparison.Ordinal);
        Assert.Contains("not on the allowlist", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchedExceptionPassesUsingItsDeclaredLicence()
    {
        ResolvedPackage package = new("dotnet-sonarscanner", "11.3.0", PackageRole.BuildAndTestOnly, "licenses/LICENSE.txt", IsSpdxExpression: false);

        LicenceGateResult result = LicenceGate.Evaluate([package], Policy);

        Assert.True(result.Success);
        ResolvedPackage passed = Assert.Single(result.Passed);
        Assert.Equal("LGPL-3.0", passed.DeclaredLicence);
    }

    [Fact]
    public void UndeterminableLicenceFails()
    {
        ResolvedPackage package = new("Microsoft.Diagnostics.Tracing.EventRegister", "1.1.28", PackageRole.Redistributed, "http://go.microsoft.com/fwlink/?LinkId=329770", IsSpdxExpression: false);

        LicenceGateResult result = LicenceGate.Evaluate([package], Policy);

        Assert.False(result.Success);
        LicenceViolation violation = Assert.Single(result.Violations);
        Assert.Equal("Microsoft.Diagnostics.Tracing.EventRegister", violation.PackageId);
        Assert.Contains("could not be determined", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionWithNoReasonIsRejectedWhenLoadingPolicy()
    {
        const string json = """
            {
              "allowedLicenses": ["MIT"],
              "exceptions": [
                { "packageId": "Whatever", "licence": "GPL-3.0", "reason": "" }
              ]
            }
            """;

        LicenceCheckException exception = Assert.Throws<LicenceCheckException>(() => LicencePolicyLoader.Load(json));
        Assert.Contains("Whatever", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no reason", exception.Message, StringComparison.Ordinal);
    }
}
