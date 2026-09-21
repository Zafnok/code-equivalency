using LicenceCheck;

CliArgs cliArgs = ArgsParser.Parse(args);
string repoRoot = Path.GetFullPath(cliArgs.RepoRoot);
string nugetPackagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

try
{
    return await GateRunner.RunAsync(repoRoot, nugetPackagesRoot, cliArgs.Fix).ConfigureAwait(false);
}
catch (LicenceCheckException exception)
{
    await Console.Error.WriteLineAsync($"licence-check: {exception.Message}").ConfigureAwait(false);
    return 1;
}
