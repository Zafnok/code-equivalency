using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LicenceCheck;

/// <summary>
/// Runs the <c>nuget-license</c> local tool (<c>.config/dotnet-tools.json</c>) against the main
/// solution and turns its JSON report into <see cref="ResolvedPackage"/>s. This covers every
/// package in every <c>packages.lock.json</c> under src/, tests/ and tools/ (ticket M0-010, AC1):
/// everything <paramref name="redistributedIds"/> names is <see cref="PackageRole.Redistributed"/>,
/// everything else reachable from the solution is <see cref="PackageRole.BuildAndTestOnly"/>.
/// </summary>
internal static class NuGetLicenseInvoker
{
    public static IReadOnlyList<ResolvedPackage> Run(string solutionPath, IReadOnlySet<string> redistributedIds)
    {
        // Invoked as a local tool via "dotnet nuget-license", the same convention this repo
        // already uses for dotnet-sonarscanner ("dotnet sonarscanner begin") and dotnet-stryker.
        ProcessStartInfo startInfo = new(ResolveDotnetHostPath())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("nuget-license");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(solutionPath);
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("Json");

        using Process process = Process.Start(startInfo)
            ?? throw new LicenceCheckException("failed to start 'nuget-license'. Is it restored (dotnet tool restore)?");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new LicenceCheckException($"'nuget-license' failed (exit {process.ExitCode.ToString(CultureInfo.InvariantCulture)}): {stderr}");
        }

        return Parse(stdout, redistributedIds);
    }

    /// <summary>
    /// Resolves the absolute path to the running 'dotnet' host, rather than trusting an unqualified
    /// "dotnet" resolved off PATH (csharpsquid:S4036). The current process is an apphost, not the
    /// dotnet muxer itself, so <c>Environment.ProcessPath</c> points at this tool's own executable and
    /// can't be used. Instead this walks up from the shared runtime directory (".../dotnet/shared/
    /// Microsoft.NETCore.App/&lt;version&gt;/", always present for a framework-dependent app) three
    /// levels to the dotnet install root, the same layout every .NET SDK and runtime installer uses.
    /// </summary>
    internal static string ResolveDotnetHostPath()
    {
        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        string hostFileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        return TryResolveDotnetHostPath(runtimeDirectory, hostFileName, File.Exists)
            ?? throw new LicenceCheckException($"could not resolve an absolute path to the 'dotnet' host from the runtime directory '{runtimeDirectory}'.");
    }

    /// <summary>Pure candidate-building logic, split out from <see cref="ResolveDotnetHostPath"/> so a test can drive every branch without depending on the real filesystem or OS.</summary>
    internal static string? TryResolveDotnetHostPath(string runtimeDirectory, string hostFileName, Func<string, bool> fileExists)
    {
        DirectoryInfo? dotnetRoot = new DirectoryInfo(runtimeDirectory).Parent?.Parent?.Parent;
        if (dotnetRoot is null)
        {
            return null;
        }

        string candidate = Path.Combine(dotnetRoot.FullName, hostFileName);
        return fileExists(candidate) ? candidate : null;
    }

    internal static IReadOnlyList<ResolvedPackage> Parse(string json, IReadOnlySet<string> redistributedIds)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<ResolvedPackage> packages = [];
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            string id = item.GetProperty("PackageId").GetString()!;
            string version = item.GetProperty("PackageVersion").GetString()!;
            string licence = item.TryGetProperty("License", out JsonElement licenceElement) ? licenceElement.GetString() ?? string.Empty : string.Empty;

            // Empirically, nuget-license 4.0.17 reports LicenseInformationOrigin == 0 exactly
            // when the package's own nuspec declared <license type="expression">: verified
            // against every package in this repo's lock files that declares one. Any other
            // value means the licence came from a licenseUrl, a bundled file, or an override,
            // none of which the gate can trust as an SPDX id without a policy exception.
            bool isSpdxExpression = item.GetProperty("LicenseInformationOrigin").GetInt32() == 0;

            PackageRole role = redistributedIds.Contains(id) ? PackageRole.Redistributed : PackageRole.BuildAndTestOnly;
            packages.Add(new ResolvedPackage(id, version, role, licence, isSpdxExpression));
        }

        return packages;
    }
}
