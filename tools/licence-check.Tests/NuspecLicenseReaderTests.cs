using LicenceCheck;

using Xunit;

namespace LicenceCheck.Tests;

public sealed class NuspecLicenseReaderTests : IDisposable
{
    private readonly string _cacheRoot = Directory.CreateTempSubdirectory("licence-check-tests-").FullName;

    public void Dispose() => Directory.Delete(_cacheRoot, recursive: true);

    [Fact]
    public void ReadsAnSpdxExpression()
    {
        WriteNuspec("Widget", "1.0.0", """<license type="expression">MIT</license>""");

        (string licence, bool isSpdxExpression) = NuspecLicenseReader.Read("Widget", "1.0.0", _cacheRoot);

        Assert.Equal("MIT", licence);
        Assert.True(isSpdxExpression);
    }

    [Fact]
    public void ReadsABundledLicenceFileAsNotAnExpression()
    {
        WriteNuspec("Widget", "1.0.0", """<license type="file">LICENSE</license>""");

        (string licence, bool isSpdxExpression) = NuspecLicenseReader.Read("Widget", "1.0.0", _cacheRoot);

        Assert.Equal("LICENSE", licence);
        Assert.False(isSpdxExpression);
    }

    [Fact]
    public void FallsBackToLicenseUrlWhenThereIsNoLicenseElement()
    {
        WriteNuspec("Widget", "1.0.0", licenseElement: null, licenseUrl: "http://example.invalid/eula");

        (string licence, bool isSpdxExpression) = NuspecLicenseReader.Read("Widget", "1.0.0", _cacheRoot);

        Assert.Equal("http://example.invalid/eula", licence);
        Assert.False(isSpdxExpression);
    }

    [Fact]
    public void ThrowsWhenTheNuspecIsMissing()
    {
        Assert.Throws<LicenceCheckException>(() => NuspecLicenseReader.Read("Nonexistent", "1.0.0", _cacheRoot));
    }

    private void WriteNuspec(string id, string version, string? licenseElement = null, string? licenseUrl = null)
    {
        string idLower = id.ToLowerInvariant();
        string directory = Path.Combine(_cacheRoot, idLower, version);
        Directory.CreateDirectory(directory);
        string licenseUrlElement = licenseUrl is null ? string.Empty : $"<licenseUrl>{licenseUrl}</licenseUrl>";
        File.WriteAllText(
            Path.Combine(directory, $"{idLower}.nuspec"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                {licenseElement}
                {licenseUrlElement}
              </metadata>
            </package>
            """);
    }
}
