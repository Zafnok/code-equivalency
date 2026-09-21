namespace LicenceCheck;

/// <summary>
/// The gate's real orchestration (file I/O, the nuget-license process, console output). Lives here
/// rather than in Program.cs's top-level statements because a top-level entry point compiles to a
/// method a test cannot call (private, not internal, regardless of InternalsVisibleTo); an
/// integration test drives this directly against the real repository instead.
/// </summary>
internal static class GateRunner
{
    public static async Task<int> RunAsync(string repoRoot, string nugetPackagesRoot, bool fix)
    {
        string policyPath = Path.Combine(repoRoot, "tools", "licence-check", "policy.json");
        LicencePolicy policy = LicencePolicyLoader.Load(await File.ReadAllTextAsync(policyPath).ConfigureAwait(false));

        PackageInventory inventory = PackageInventoryBuilder.Build(repoRoot, nugetPackagesRoot);
        IReadOnlyList<ResolvedPackage> fromSolution = NuGetLicenseInvoker.Run(Path.Combine(repoRoot, "Equiv.slnx"), inventory.RedistributedIds);

        List<ResolvedPackage> allPackages = [.. fromSolution, .. inventory.ExtraPackages];
        LicenceGateResult result = LicenceGate.Evaluate(allPackages, policy);

        if (!result.Success)
        {
            foreach (string line in ViolationReport.FormatLines(result.Violations))
            {
                await Console.Error.WriteLineAsync(line).ConfigureAwait(false);
            }

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

    /// <summary>Returns a process exit code if the freshness check fails, or null if it passed (or was skipped).</summary>
    private static async Task<int?> CheckNoticesAsync(string repoRoot, PackageInventory inventory, LicenceGateResult result, bool fix)
    {
        string notices = ThirdPartyNoticesRenderer.Render(result.Passed);
        string noticesPath = Path.Combine(repoRoot, "THIRD-PARTY-NOTICES.md");
        string? existing = File.Exists(noticesPath) ? await File.ReadAllTextAsync(noticesPath).ConfigureAwait(false) : null;

        NoticesDecision decision = NoticesChecker.Decide(inventory.SamplesFullyRestored, repoRoot, noticesPath, existing, notices, fix);
        switch (decision.Outcome)
        {
            case NoticesOutcome.Regenerated:
                await File.WriteAllTextAsync(noticesPath, notices).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(decision.Message).ConfigureAwait(false);
                return null;
            case NoticesOutcome.OutOfDate:
                await Console.Error.WriteLineAsync(decision.Message).ConfigureAwait(false);
                return 1;
            case NoticesOutcome.Skipped:
                await Console.Out.WriteLineAsync(decision.Message).ConfigureAwait(false);
                return null;
            case NoticesOutcome.UpToDate:
            default:
                return null;
        }
    }
}
