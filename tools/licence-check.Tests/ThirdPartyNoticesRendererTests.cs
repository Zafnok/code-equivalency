using LicenceCheck;

using Xunit;

using static VerifyXunit.Verifier;

namespace LicenceCheck.Tests;

public sealed class ThirdPartyNoticesRendererTests
{
    [Fact]
    public Task RendersAllThreeSections()
    {
        ResolvedPackage[] packages =
        [
            new("Sarif.Sdk", "5.7.0", PackageRole.Redistributed, "MIT", IsSpdxExpression: true),
            new("Newtonsoft.Json", "13.0.3", PackageRole.Redistributed, "MIT", IsSpdxExpression: true),
            new("xunit.v3", "4.0.1", PackageRole.BuildAndTestOnly, "Apache-2.0", IsSpdxExpression: true),
            new("dotnet-sonarscanner", "11.3.0", PackageRole.BuildAndTestOnly, "LGPL-3.0", IsSpdxExpression: false),
            new("Microsoft.AspNetCore.Mvc.Core", "2.3.13", PackageRole.SamplesOnly, "Apache-2.0", IsSpdxExpression: false),
        ];

        string notices = ThirdPartyNoticesRenderer.Render(packages);

        return Verify(notices);
    }

    [Fact]
    public Task RendersAnEmptySamplesOnlySection()
    {
        ResolvedPackage[] packages =
        [
            new("Sarif.Sdk", "5.7.0", PackageRole.Redistributed, "MIT", IsSpdxExpression: true),
        ];

        string notices = ThirdPartyNoticesRenderer.Render(packages);

        return Verify(notices);
    }
}
