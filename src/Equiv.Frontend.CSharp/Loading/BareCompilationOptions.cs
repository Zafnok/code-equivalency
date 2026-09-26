using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// A non-SDK project's parse and compilation options (M3-029): what MSBuild's <c>Csc</c> task passes, with MSBuild's
/// defaults for .NET Framework. LangVersion is 7.3 unless set. The platform is applied exactly, since
/// <c>BoundSerialiser</c> reads it for x87: <c>Prefer32Bit</c> defaults to true for an executable on .NET Framework 4.5
/// or later, and turns <c>AnyCPU</c> into <c>AnyCPU32BitPreferred</c> for an executable only, as <c>Csc</c> does.
/// </summary>
internal static class BareCompilationOptions
{
    private static readonly Version DotNet45 = new(4, 5);

    public static CSharpParseOptions Parse(MsBuildProperties properties)
    {
        string langVersion = properties.Read("LangVersion").Trim();
        LanguageVersion version = langVersion.Length == 0 ? LanguageVersion.CSharp7_3
            : LanguageVersionFacts.TryParse(langVersion, out LanguageVersion parsed) ? parsed
            : throw new UnsupportedConstructException($"the LangVersion '{langVersion}', which is not a C# language version");
        return new CSharpParseOptions(
            version,
            properties.Read("DocumentationFile").Trim().Length > 0 ? DocumentationMode.Diagnose : DocumentationMode.Parse,
            SourceCodeKind.Regular,
            Split(properties.Read("DefineConstants"), ' ').Distinct(StringComparer.Ordinal));
    }

    public static CSharpCompilationOptions Compilation(MsBuildProperties properties, Version frameworkVersion)
    {
        OutputKind kind = properties.Read("OutputType").Trim().ToUpperInvariant() switch
        {
            "EXE" => OutputKind.ConsoleApplication,
            "WINEXE" => OutputKind.WindowsApplication,
            "APPCONTAINEREXE" => OutputKind.WindowsRuntimeApplication,
            "MODULE" => OutputKind.NetModule,
            "WINMDOBJ" => OutputKind.WindowsRuntimeMetadata,
            _ => OutputKind.DynamicallyLinkedLibrary,
        };
        bool executable = kind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication or OutputKind.WindowsRuntimeApplication;
        string prefer32Bit = properties.Read("Prefer32Bit").Trim();
        bool preferred = prefer32Bit.Length == 0 ? executable && frameworkVersion >= DotNet45 : prefer32Bit.Equals("true", StringComparison.OrdinalIgnoreCase);
        string platformTarget = properties.Read("PlatformTarget").Trim();
        Platform platform = platformTarget.ToUpperInvariant() switch
        {
            "" or "ANYCPU" => preferred && executable ? Platform.AnyCpu32BitPreferred : Platform.AnyCpu,
            "ANYCPU32BITPREFERRED" => Platform.AnyCpu32BitPreferred,
            "X86" => Platform.X86,
            "X64" => Platform.X64,
            "ITANIUM" => Platform.Itanium,
            "ARM" => Platform.Arm,
            "ARM64" => Platform.Arm64,
            _ => throw new UnsupportedConstructException($"the PlatformTarget '{platformTarget}', which is not a platform"),
        };

        Dictionary<string, ReportDiagnostic> specific = new(StringComparer.Ordinal);
        foreach ((string property, ReportDiagnostic report) in (ReadOnlySpan<(string, ReportDiagnostic)>)[("WarningsNotAsErrors", ReportDiagnostic.Warn), ("WarningsAsErrors", ReportDiagnostic.Error), ("NoWarn", ReportDiagnostic.Suppress)])
        {
            foreach (string id in Split(properties.Read(property)))
            {
                specific[DiagnosticId(id)] = report;
            }
        }

        string warningLevel = properties.Read("WarningLevel").Trim();
        return new CSharpCompilationOptions(
            kind,
            mainTypeName: properties.Read("StartupObject").Trim() is { Length: > 0 } main ? main : null,
            optimizationLevel: properties.IsTrue("Optimize") ? OptimizationLevel.Release : OptimizationLevel.Debug,
            checkOverflow: properties.IsTrue("CheckForOverflowUnderflow"),
            allowUnsafe: properties.IsTrue("AllowUnsafeBlocks"),
            platform: platform,
            generalDiagnosticOption: properties.IsTrue("TreatWarningsAsErrors") ? ReportDiagnostic.Error : ReportDiagnostic.Default,
            warningLevel: warningLevel.Length == 0 ? 4
                : int.TryParse(warningLevel, NumberStyles.None, CultureInfo.InvariantCulture, out int level) ? level
                : throw new UnsupportedConstructException($"the WarningLevel '{warningLevel}', which is not a number"),
            specificDiagnosticOptions: specific,
            deterministic: properties.IsTrue("Deterministic"),
            nullableContextOptions: properties.Read("Nullable").Trim().ToUpperInvariant() switch
            {
                "ENABLE" => NullableContextOptions.Enable,
                "WARNINGS" => NullableContextOptions.Warnings,
                "ANNOTATIONS" => NullableContextOptions.Annotations,
                _ => NullableContextOptions.Disable,
            },
            assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);
    }

    /// <summary>A bare warning number is a C# one, as the compiler's command line reads it: <c>1591</c> is <c>CS1591</c>.</summary>
    private static string DiagnosticId(string id) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? "CS" + number.ToString("0000", CultureInfo.InvariantCulture) : id;

    private static string[] Split(string list, params char[] more) =>
        list.Split([';', ',', .. more], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
