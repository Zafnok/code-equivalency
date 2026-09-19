namespace Equiv.Cli;

/// <summary>Process exit codes for <c>equiv compare</c> (ARCHITECTURE.md's "Equiv.Cli" exit-codes list).</summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int Divergent = 1;
    public const int UnknownPresent = 2;
    public const int UsageError = 3;
    public const int LoadFailure = 4;
}
