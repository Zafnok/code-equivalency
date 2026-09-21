using LicenceCheck;

using Xunit;

namespace LicenceCheck.Tests;

public sealed class LicencePolicyLoaderTests
{
    [Fact]
    public void LoadsAllowedLicensesAndExceptions()
    {
        const string json = """
            {
              "allowedLicenses": ["MIT", "Apache-2.0"],
              "exceptions": [
                { "packageId": "dotnet-sonarscanner", "licence": "LGPL-3.0", "reason": "CI process only." }
              ]
            }
            """;

        LicencePolicy policy = LicencePolicyLoader.Load(json);

        Assert.Equal(["MIT", "Apache-2.0"], policy.AllowedLicenses);
        LicenceException exception = Assert.Single(policy.Exceptions);
        Assert.Equal("dotnet-sonarscanner", exception.PackageId);
        Assert.Equal("LGPL-3.0", exception.Licence);
        Assert.Equal("CI process only.", exception.Reason);
    }

    [Fact]
    public void ToleratesNoExceptionsSection()
    {
        const string json = """{ "allowedLicenses": ["MIT"] }""";

        LicencePolicy policy = LicencePolicyLoader.Load(json);

        Assert.Equal(["MIT"], policy.AllowedLicenses);
        Assert.Empty(policy.Exceptions);
    }

    [Fact]
    public void MissingPackageIdOnAnExceptionIsRejected()
    {
        const string json = """
            {
              "allowedLicenses": [],
              "exceptions": [ { "licence": "LGPL-3.0", "reason": "because" } ]
            }
            """;

        Assert.Throws<LicenceCheckException>(() => LicencePolicyLoader.Load(json));
    }

    [Fact]
    public void FindExceptionIsCaseInsensitiveOnPackageId()
    {
        LicencePolicy policy = new([], [new LicenceException("Dotnet-Sonarscanner", "LGPL-3.0", "reason")]);

        Assert.NotNull(policy.FindException("dotnet-sonarscanner"));
        Assert.Null(policy.FindException("something-else"));
    }
}
