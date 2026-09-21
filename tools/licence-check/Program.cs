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

    Console.WriteLine($"licence-check: {result.Passed.Count} package(s) passed the dependency licence gate.");
    return 0;
}
catch (LicenceCheckException exception)
{
    Console.Error.WriteLine($"licence-check: {exception.Message}");
    return 1;
}

static string Normalize(string? text) => (text ?? string.Empty).ReplaceLineEndings("\n");
