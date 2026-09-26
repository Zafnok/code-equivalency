using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class BareCompilationOptionsTests
{
    [Theory]
    [InlineData("Library", "", "", "v4.8", Platform.AnyCpu)]
    [InlineData("Exe", "", "", "v4.8", Platform.AnyCpu32BitPreferred)]
    [InlineData("WinExe", "", "", "v4.5", Platform.AnyCpu32BitPreferred)]
    [InlineData("AppContainerExe", "AnyCPU", "", "v4.8", Platform.AnyCpu32BitPreferred)]
    [InlineData("Exe", "", "", "v4.0", Platform.AnyCpu)]
    [InlineData("Exe", "", "false", "v4.8", Platform.AnyCpu)]
    [InlineData("Exe", "anycpu", "TRUE", "v4.0", Platform.AnyCpu32BitPreferred)]
    [InlineData("Library", "", "true", "v4.8", Platform.AnyCpu)]
    [InlineData("Exe", "x86", "", "v4.8", Platform.X86)]
    [InlineData("Library", "x64", "", "v4.8", Platform.X64)]
    [InlineData("Library", "Itanium", "", "v4.8", Platform.Itanium)]
    [InlineData("Library", "ARM", "", "v4.8", Platform.Arm)]
    [InlineData("Library", "arm64", "", "v4.8", Platform.Arm64)]
    [InlineData("Library", "AnyCPU32BitPreferred", "", "v4.8", Platform.AnyCpu32BitPreferred)]
    public void PlatformAndPrefer32BitAreApplied(string outputType, string platformTarget, string prefer32Bit, string frameworkVersion, Platform expected)
    {
        MsBuildProperties properties = Properties(("OutputType", outputType), ("PlatformTarget", platformTarget), ("Prefer32Bit", prefer32Bit));

        Assert.Equal(expected, BareCompilationOptions.Compilation(properties, Version.Parse(frameworkVersion[1..])).Platform);
    }

    [Theory]
    [InlineData("", OutputKind.DynamicallyLinkedLibrary)]
    [InlineData("Library", OutputKind.DynamicallyLinkedLibrary)]
    [InlineData("exe", OutputKind.ConsoleApplication)]
    [InlineData("WinExe", OutputKind.WindowsApplication)]
    [InlineData("AppContainerExe", OutputKind.WindowsRuntimeApplication)]
    [InlineData("Module", OutputKind.NetModule)]
    [InlineData("WinMDObj", OutputKind.WindowsRuntimeMetadata)]
    public void OutputTypeMapsToAnOutputKind(string outputType, OutputKind expected) =>
        Assert.Equal(expected, BareCompilationOptions.Compilation(Properties(("OutputType", outputType)), new Version(4, 8)).OutputKind);

    [Fact]
    public void TheDefaultsAreMsBuildsForDotNetFramework()
    {
        CSharpCompilationOptions options = BareCompilationOptions.Compilation(Properties(), new Version(4, 8));
        CSharpParseOptions parse = BareCompilationOptions.Parse(Properties());

        Assert.Equal(LanguageVersion.CSharp7_3, parse.LanguageVersion);
        Assert.Equal(DocumentationMode.Parse, parse.DocumentationMode);
        Assert.Empty(parse.PreprocessorSymbolNames);
        Assert.Equal(4, options.WarningLevel);
        Assert.Equal(ReportDiagnostic.Default, options.GeneralDiagnosticOption);
        Assert.Empty(options.SpecificDiagnosticOptions);
        Assert.Equal(OptimizationLevel.Debug, options.OptimizationLevel);
        Assert.False(options.CheckOverflow);
        Assert.False(options.AllowUnsafe);
        Assert.False(options.Deterministic);
        Assert.Null(options.MainTypeName);
        Assert.Equal(NullableContextOptions.Disable, options.NullableContextOptions);
        Assert.Same(DesktopAssemblyIdentityComparer.Default, options.AssemblyIdentityComparer);
    }

    [Fact]
    public void TheProjectsSettingsAreApplied()
    {
        MsBuildProperties properties = Properties(
            ("LangVersion", "latest"),
            ("DocumentationFile", @"bin\Debug\P.xml"),
            ("DefineConstants", "DEBUG;TRACE, EXTRA DEBUG"),
            ("WarningLevel", "2"),
            ("TreatWarningsAsErrors", "true"),
            ("WarningsAsErrors", "CS0168;0618"),
            ("WarningsNotAsErrors", "612"),
            ("NoWarn", "1591, CS0168"),
            ("Optimize", "true"),
            ("CheckForOverflowUnderflow", "true"),
            ("AllowUnsafeBlocks", "True"),
            ("Deterministic", "true"),
            ("StartupObject", "App.Program"),
            ("Nullable", "enable"));

        CSharpCompilationOptions options = BareCompilationOptions.Compilation(properties, new Version(4, 8));
        CSharpParseOptions parse = BareCompilationOptions.Parse(properties);

        Assert.Equal(LanguageVersion.Latest, parse.SpecifiedLanguageVersion);
        Assert.Equal(DocumentationMode.Diagnose, parse.DocumentationMode);
        Assert.Equal(["DEBUG", "TRACE", "EXTRA"], parse.PreprocessorSymbolNames, StringComparer.Ordinal);
        Assert.Equal(2, options.WarningLevel);
        Assert.Equal(ReportDiagnostic.Error, options.GeneralDiagnosticOption);
        Assert.Equal(
            [("CS0168", ReportDiagnostic.Suppress), ("CS0612", ReportDiagnostic.Warn), ("CS0618", ReportDiagnostic.Error), ("CS1591", ReportDiagnostic.Suppress)],
            options.SpecificDiagnosticOptions.OrderBy(static p => p.Key, StringComparer.Ordinal).Select(static p => (p.Key, p.Value)));
        Assert.Equal(OptimizationLevel.Release, options.OptimizationLevel);
        Assert.True(options.CheckOverflow);
        Assert.True(options.AllowUnsafe);
        Assert.True(options.Deterministic);
        Assert.Equal("App.Program", options.MainTypeName);
        Assert.Equal(NullableContextOptions.Enable, options.NullableContextOptions);
    }

    [Theory]
    [InlineData("warnings", NullableContextOptions.Warnings)]
    [InlineData("Annotations", NullableContextOptions.Annotations)]
    [InlineData("disable", NullableContextOptions.Disable)]
    public void NullableMapsToAContext(string nullable, NullableContextOptions expected) =>
        Assert.Equal(expected, BareCompilationOptions.Compilation(Properties(("Nullable", nullable)), new Version(4, 8)).NullableContextOptions);

    [Theory]
    [InlineData("LangVersion", "8.5", "the LangVersion '8.5', which is not a C# language version")]
    [InlineData("PlatformTarget", "mips", "the PlatformTarget 'mips', which is not a platform")]
    [InlineData("WarningLevel", "high", "the WarningLevel 'high', which is not a number")]
    public void AValueTheCompilerWouldRejectIsUnsupported(string property, string value, string construct)
    {
        MsBuildProperties properties = Properties((property, value));

        UnsupportedConstructException exception = Assert.Throws<UnsupportedConstructException>(() =>
        {
            BareCompilationOptions.Parse(properties);
            BareCompilationOptions.Compilation(properties, new Version(4, 8));
        });

        Assert.Equal(construct, exception.Construct);
    }

    private static MsBuildProperties Properties(params (string Name, string Value)[] values)
    {
        MsBuildProperties properties = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), static _ => null);
        foreach ((string name, string value) in values)
        {
            properties.Set(name, new PropertyValue(value));
        }

        return properties;
    }
}
