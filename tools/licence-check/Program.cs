using LicenceCheck;

string repoRoot = ".";
bool fix = false;

for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "--repo-root", StringComparison.Ordinal) && i + 1 < args.Length)
    {
        repoRoot = args[++i];
    }
    else if (string.Equals(args[i], "--fix", StringComparison.Ordinal))
    {
        fix = true;
    }
}

repoRoot = Path.GetFullPath(repoRoot);
string nugetPackagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

try
{
    string policyPath = Path.Combine(repoRoot, "tools", "licence-check", "policy.json");
    LicencePolicy policy = LicencePolicyLoader.Load(File.ReadAllText(policyPath));

    PackageInventory inventory = PackageInventoryBuilder.Build(repoRoot, nugetPackagesRoot);
    IReadOnlyList<ResolvedPackage> fromSolution = NuGetLicenseInvoker.Run(Path.Combine(repoRoot, "Equiv.slnx"), inventory.RedistributedIds);

    List<ResolvedPackage> allPackages = [.. fromSolution, .. inventory.ExtraPackages];
    LicenceGateResult result = LicenceGate.Evaluate(allPackages, policy);

    if (!result.Success)
    {
        Console.Error.WriteLine($"licence-check: {result.Violations.Count} package(s) failed the dependency licence gate:");
        foreach (LicenceViolation violation in result.Violations)
        {
            Console.Error.WriteLine($"  - {violation}");
        }

        return 1;
    }

    if (!inventory.SamplesFullyRestored)
    {
        // samples/ is only restored under build.ps1 -Integration (the legacy side needs
        // MSBuild.exe, Windows-only). This run's resolved package set is a real subset of the
        // truth, so comparing it against THIRD-PARTY-NOTICES.md would report false drift (or,
        // with --fix, overwrite the tracked file with an incomplete one). Skip the check.
        Console.WriteLine($"licence-check: samples/ was not restored under '{repoRoot}' in this run; skipping the THIRD-PARTY-NOTICES.md freshness check.");
    }
    else
    {
        string notices = ThirdPartyNoticesRenderer.Render(result.Passed);
        string noticesPath = Path.Combine(repoRoot, "THIRD-PARTY-NOTICES.md");
        string? existing = File.Exists(noticesPath) ? File.ReadAllText(noticesPath) : null;

        if (!string.Equals(Normalize(existing), Normalize(notices), StringComparison.Ordinal))
        {
            if (fix)
            {
                File.WriteAllText(noticesPath, notices);
                Console.WriteLine($"licence-check: regenerated {noticesPath}.");
            }
            else
            {
                Console.Error.WriteLine($"licence-check: {noticesPath} is out of date. Run with --fix to regenerate it.");
                return 1;
            }
        }
    }

    Console.WriteLine($"licence-check: {result.Passed.Count} package(s) passed the dependency licence gate.");
    return 0;
}
catch (LicenceCheckException exception)
{
    Console.Error.WriteLine($"licence-check: {exception.Message}");
    return 1;
}

static string Normalize(string? text) => (text ?? string.Empty).ReplaceLineEndings("\n");
