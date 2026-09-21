using LicenceCheck;

(string repoRoot, bool fix) = ParseArgs(args);
repoRoot = Path.GetFullPath(repoRoot);
string nugetPackagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

try
{
    return await RunGateAsync(repoRoot, nugetPackagesRoot, fix).ConfigureAwait(false);
}
catch (LicenceCheckException exception)
{
    await Console.Error.WriteLineAsync($"licence-check: {exception.Message}").ConfigureAwait(false);
    return 1;
}

static (string RepoRoot, bool Fix) ParseArgs(string[] args)
{
    string repoRoot = ".";
    bool fix = false;
    int i = 0;
    while (i < args.Length)
    {
        if (string.Equals(args[i], "--repo-root", StringComparison.Ordinal) && i + 1 < args.Length)
        {
            repoRoot = args[i + 1];
            i += 2;
        }
        else if (string.Equals(args[i], "--fix", StringComparison.Ordinal))
        {
            fix = true;
            i += 1;
        }
        else
        {
            i += 1;
        }
    }

    return (repoRoot, fix);
}

static async Task<int> RunGateAsync(string repoRoot, string nugetPackagesRoot, bool fix)
{
    string policyPath = Path.Combine(repoRoot, "tools", "licence-check", "policy.json");
    LicencePolicy policy = LicencePolicyLoader.Load(await File.ReadAllTextAsync(policyPath).ConfigureAwait(false));

    PackageInventory inventory = PackageInventoryBuilder.Build(repoRoot, nugetPackagesRoot);
    IReadOnlyList<ResolvedPackage> fromSolution = NuGetLicenseInvoker.Run(Path.Combine(repoRoot, "Equiv.slnx"), inventory.RedistributedIds);

    List<ResolvedPackage> allPackages = [.. fromSolution, .. inventory.ExtraPackages];
    LicenceGateResult result = LicenceGate.Evaluate(allPackages, policy);

    if (!result.Success)
    {
        await ReportViolationsAsync(result.Violations).ConfigureAwait(false);
        return 1;
    }

    int? noticesFailure = await CheckNoticesAsync(repoRoot, inventory, result, fix).ConfigureAwait(false);
    if (noticesFailure is int exitCode)
    {
        return exitCode;
    }

    await Console.Out.WriteLineAsync($"licence-check: {result.Passed.Count} package(s) passed the dependency licence gate.").ConfigureAwait(false);
    return 0;
}

static async Task ReportViolationsAsync(IReadOnlyList<LicenceViolation> violations)
{
    await Console.Error.WriteLineAsync($"licence-check: {violations.Count} package(s) failed the dependency licence gate:").ConfigureAwait(false);
    foreach (LicenceViolation violation in violations)
    {
        await Console.Error.WriteLineAsync($"  - {violation}").ConfigureAwait(false);
    }
}

/// <summary>Returns a process exit code if the freshness check fails, or null if it passed (or was skipped).</summary>
static async Task<int?> CheckNoticesAsync(string repoRoot, PackageInventory inventory, LicenceGateResult result, bool fix)
{
    if (!inventory.SamplesFullyRestored)
    {
        // samples/ is only restored under build.ps1 -Integration (the legacy side needs
        // MSBuild.exe, Windows-only). This run's resolved package set is a real subset of the
        // truth, so comparing it against THIRD-PARTY-NOTICES.md would report false drift (or,
        // with --fix, overwrite the tracked file with an incomplete one). Skip the check.
        await Console.Out.WriteLineAsync($"licence-check: samples/ was not restored under '{repoRoot}' in this run; skipping the THIRD-PARTY-NOTICES.md freshness check.").ConfigureAwait(false);
        return null;
    }

    string notices = ThirdPartyNoticesRenderer.Render(result.Passed);
    string noticesPath = Path.Combine(repoRoot, "THIRD-PARTY-NOTICES.md");
    string? existing = File.Exists(noticesPath) ? await File.ReadAllTextAsync(noticesPath).ConfigureAwait(false) : null;

    if (string.Equals(Normalize(existing), Normalize(notices), StringComparison.Ordinal))
    {
        return null;
    }

    if (!fix)
    {
        await Console.Error.WriteLineAsync($"licence-check: {noticesPath} is out of date. Run with --fix to regenerate it.").ConfigureAwait(false);
        return 1;
    }

    await File.WriteAllTextAsync(noticesPath, notices).ConfigureAwait(false);
    await Console.Out.WriteLineAsync($"licence-check: regenerated {noticesPath}.").ConfigureAwait(false);
    return null;
}

static string Normalize(string? text) => (text ?? string.Empty).ReplaceLineEndings("\n");
